using System.Collections.Concurrent;
using System.Security.Claims;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Controllers
{
    public class PropertiesController : Controller
    {
        private readonly IPropertyRepository _propRepo;
        private readonly IBookingRepository _bookingRepo;
        private readonly IAvailabilitySuggestionService _availabilityService;
        private readonly ICalendarRangeService _calendarRangeService;

        private static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _rateLimits = new();

        public static void ResetRateLimits() => _rateLimits.Clear();

        public PropertiesController(
            IPropertyRepository propRepo, 
            IBookingRepository bookingRepo,
            IAvailabilitySuggestionService? availabilityService = null,
            ICalendarRangeService? calendarRangeService = null)
        {
            _propRepo = propRepo;
            _bookingRepo = bookingRepo;
            _availabilityService = availabilityService ?? new AvailabilitySuggestionService(bookingRepo);
            _calendarRangeService = calendarRangeService ?? new CalendarRangeService(bookingRepo);
        }

        public async Task<IActionResult> Index(
            string? location, 
            int? typeId, 
            int? occasionId, 
            decimal? minPrice, 
            decimal? maxPrice, 
            int? guests, 
            DateTime? checkIn = null,
            DateTime? checkOut = null,
            string? sortBy = "popular",
            List<int>? typeIds = null,
            List<int>? amenityIds = null,
            int page = 1, 
            int pageSize = 12)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 25);

            // If single typeId is provided, merge into typeIds list
            if (typeId.HasValue && (typeIds == null || !typeIds.Contains(typeId.Value)))
            {
                typeIds ??= new List<int>();
                typeIds.Add(typeId.Value);
            }

            bool hasValidDateRange = checkIn.HasValue && checkOut.HasValue && checkOut.Value.Date > checkIn.Value.Date;
            if (checkIn.HasValue && checkOut.HasValue && checkOut.Value.Date <= checkIn.Value.Date)
            {
                TempData["Error"] = "Check-out date must be after check-in date.";
            }

            var (properties, totalCount) = await _propRepo.SearchPropertiesPagedAsync(
                location, typeId, occasionId, minPrice, maxPrice, guests, page, pageSize, sortBy, typeIds, amenityIds,
                hasValidDateRange ? checkIn!.Value.Date : null,
                hasValidDateRange ? checkOut!.Value.Date : null);

            if (hasValidDateRange)
            {
                var availabilityMap = new Dictionary<int, AvailabilitySuggestionResult>();
                var availableProperties = new List<Property>();
                foreach (var p in properties)
                {
                    var avail = await _availabilityService.GetNearestSuggestionsAsync(p.PropertyID, checkIn!.Value, checkOut!.Value);
                    availabilityMap[p.PropertyID] = avail;
                    if (avail.IsRequestedAvailable)
                    {
                        availableProperties.Add(p);
                    }
                }
                if (availableProperties.Count != properties.Count)
                {
                    int removed = properties.Count - availableProperties.Count;
                    totalCount = Math.Max(0, totalCount - removed);
                    properties = availableProperties;
                }
                ViewBag.PropertyAvailability = availabilityMap;
            }

            ViewBag.PropertyTypes = await _propRepo.GetPropertyTypesAsync();
            ViewBag.Amenities = await _propRepo.GetAllAmenitiesAsync();
            ViewBag.Occasions = await _propRepo.GetOccasionsAsync();
            ViewBag.SelectedLocation = location;
            ViewBag.SelectedTypeId = typeId;
            ViewBag.SelectedTypeIds = typeIds ?? new List<int>();
            ViewBag.SelectedAmenityIds = amenityIds ?? new List<int>();
            ViewBag.SelectedOccasionId = occasionId;
            ViewBag.SelectedMinPrice = minPrice;
            ViewBag.SelectedMaxPrice = maxPrice;
            ViewBag.SelectedGuests = guests;
            ViewBag.SelectedCheckIn = checkIn?.ToString("yyyy-MM-dd");
            ViewBag.SelectedCheckOut = checkOut?.ToString("yyyy-MM-dd");
            ViewBag.SelectedSort = string.IsNullOrEmpty(sortBy) ? "popular" : sortBy;
            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = totalCount;
            ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSize));

            return View(properties);
        }

        public async Task<IActionResult> Details(int id, DateTime? checkIn = null, DateTime? checkOut = null)
        {
            var property = await _propRepo.GetByIdAsync(id);
            if (property == null) return NotFound();

            // Owners and staff see their own properties in every state, including Unpublished. Staff see all properties.
            bool isAuthorizedViewer = false;
            if (User?.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole("Admin") || User.IsInRole("Employee"))
                {
                    isAuthorizedViewer = true;
                }
                else if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int currentUserId) && currentUserId == property.OwnerID)
                {
                    isAuthorizedViewer = true;
                }
            }

            if (!isAuthorizedViewer)
            {
                bool isVisible = await _propRepo.IsPropertyPubliclyVisibleAsync(property);
                if (!isVisible)
                {
                    return NotFound();
                }
            }

            var reviews = await _bookingRepo.GetReviewsByPropertyIdAsync(id);
            ViewBag.Reviews = reviews;
            ViewBag.SelectedCheckIn = checkIn?.ToString("yyyy-MM-dd");
            ViewBag.SelectedCheckOut = checkOut?.ToString("yyyy-MM-dd");

            if (checkIn.HasValue && checkOut.HasValue && checkOut.Value.Date > checkIn.Value.Date)
            {
                var avail = await _availabilityService.GetNearestSuggestionsAsync(id, checkIn.Value, checkOut.Value);
                ViewBag.AvailabilityResult = avail;
            }

            return View(property);
        }

        [HttpGet("/api/properties/{id}/availability/suggestions")]
        public async Task<IActionResult> GetAvailabilitySuggestions(
            int id,
            [FromQuery] DateTime? checkIn,
            [FromQuery] DateTime? checkOut,
            [FromQuery] string bookingType = "FullDay",
            [FromQuery] string? slotType = null)
        {
            // Rate-limiting check: 60 requests per minute per IP / User
            string clientKey = User?.Identity?.IsAuthenticated == true
                ? $"user_{User.FindFirstValue(ClaimTypes.NameIdentifier)}"
                : $"ip_{HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "anon"}";

            var now = DateTime.UtcNow;
            var rateEntry = _rateLimits.AddOrUpdate(
                clientKey,
                _ => (1, now),
                (_, current) => (now - current.WindowStart).TotalMinutes < 1.0
                    ? (current.Count + 1, current.WindowStart)
                    : (1, now));

            if (rateEntry.Count > 60)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    error = "Rate limit exceeded. Please try again in a moment.",
                    retryAfterSeconds = 60
                });
            }

            // Input validation
            if (id <= 0)
            {
                return BadRequest(new { error = "Invalid property ID." });
            }

            var prop = await _propRepo.GetByIdAsync(id);
            if (prop == null)
            {
                return NotFound(new { error = "Property not found." });
            }

            if (!checkIn.HasValue || !checkOut.HasValue)
            {
                return BadRequest(new { error = "Both checkIn and checkOut dates are required." });
            }

            if (checkOut.Value.Date <= checkIn.Value.Date)
            {
                return BadRequest(new { error = "CheckOut date must be strictly after CheckIn date." });
            }

            var suggestions = await _availabilityService.GetNearestSuggestionsAsync(
                id, checkIn.Value, checkOut.Value, bookingType, slotType);

            return Ok(suggestions);
        }

        [HttpGet("/api/properties/{id}/booked-dates")]
        public async Task<IActionResult> GetBookedDates(int id)
        {
            if (id <= 0) return BadRequest(new { error = "Invalid property ID." });
            var blocked = await _calendarRangeService.GetBlockedDatesForPropertyAsync(id);
            return Ok(blocked);
        }
    }
}
