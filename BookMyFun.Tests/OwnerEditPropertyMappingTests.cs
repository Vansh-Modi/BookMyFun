using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Xunit;

namespace BookMyFun.Tests
{
    public class OwnerEditPropertyMappingTests
    {
        private class NullTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private class StubPropRepo : DispatchProxy
        {
            public Property? StoredProperty { get; set; }
            public bool UpdatedCalled { get; private set; }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                var name = targetMethod!.Name;
                if (name == nameof(IPropertyRepository.GetByIdAsync))
                    return Task.FromResult(StoredProperty);
                if (name == nameof(IPropertyRepository.UpdatePropertyAsync))
                {
                    UpdatedCalled = true;
                    return Task.FromResult(true);
                }
                if (name == nameof(IPropertyRepository.GetPropertyTypesAsync))
                    return Task.FromResult(new List<PropertyType>());
                if (name == nameof(IPropertyRepository.GetOccasionsAsync))
                    return Task.FromResult(new List<Occasion>());
                if (name == nameof(IPropertyRepository.GetAllAmenitiesAsync))
                    return Task.FromResult(new List<AmenityMaster>());
                if (name == nameof(IPropertyRepository.GetPropertyMediaAsync))
                    return Task.FromResult(StoredProperty?.MediaList ?? new List<PropertyMedia>());
                if (name == nameof(IPropertyRepository.SetPropertyOccasionsAsync) ||
                    name == nameof(IPropertyRepository.SetPropertyAmenitiesAsync))
                    return Task.FromResult(true);
                if (name == nameof(IPropertyRepository.AddPropertyImageAsync))
                    return Task.FromResult(1);
                return null;
            }

            public static StubPropRepo Create(Property? prop)
            {
                var proxy = DispatchProxy.Create<IPropertyRepository, StubPropRepo>();
                var impl = (StubPropRepo)(object)proxy;
                impl.StoredProperty = prop;
                return impl;
            }
        }

        private static OwnerController CreateController(IPropertyRepository propRepo, int ownerId = 5)
        {
            var controller = new OwnerController(propRepo, null!, null!, null!, null!, null!, null!, null!);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, ownerId.ToString()),
                        new Claim(ClaimTypes.Role, "Owner")
                    }, "Test"))
                }
            };
            controller.TempData = new TempDataDictionary(controller.HttpContext, new NullTempDataProvider());
            return controller;
        }

        [Fact]
        public async Task EditProperty_Get_FillsAllSavedFieldsIncludingMediaAmenitiesOccasions()
        {
            var prop = new Property
            {
                PropertyID = 42,
                OwnerID = 5,
                PropertyTypeID = 2,
                Title = "Emerald Farmhouse",
                Description = "Lush green retreat",
                Location = "Karjat, Maharashtra",
                City = "Karjat",
                State = "Maharashtra",
                Pincode = "410201",
                NearbyAreaOrRoad = "Old Highway",
                MapUrl = "https://maps.google.com/?q=18.9,73.3",
                CaretakerName = "Ramesh",
                CaretakerPhone = "9876543210",
                OwnerIsCaretaker = false,
                MinStayNights = 2,
                MaxStayNights = 14,
                PricePerNight = 12500m,
                MaxGuests = 20,
                FreeCancellationWindowHours = 72,
                CancellationCutoffHours = 24,
                CancellationFeeType = "fixed",
                CancellationFeeValue = 1500m,
                PrimaryImageUrl = "/uploads/media/photo1.jpg",
                AmenityIDs = new List<int> { 1, 3 },
                OccasionIDs = new List<int> { 2, 4 },
                MediaList = new List<PropertyMedia>
                {
                    new PropertyMedia { MediaID = 1, MediaType = "photo", StorageUrl = "/uploads/media/photo1.jpg", IsPrimary = true },
                    new PropertyMedia { MediaID = 2, MediaType = "video", StorageUrl = "/uploads/media/tour.mp4" }
                }
            };

            var stub = StubPropRepo.Create(prop);
            var controller = CreateController((IPropertyRepository)(object)stub);

            var res = await controller.EditProperty(42);
            var view = Assert.IsType<ViewResult>(res);
            var vm = Assert.IsType<PropertyEditViewModel>(view.Model);

            Assert.Equal("Emerald Farmhouse", vm.Title);
            Assert.Equal("Lush green retreat", vm.Description);
            Assert.Equal("Karjat, Maharashtra", vm.Location);
            Assert.Equal("Karjat", vm.City);
            Assert.Equal("Maharashtra", vm.State);
            Assert.Equal("410201", vm.Pincode);
            Assert.Equal("Old Highway", vm.NearbyAreaOrRoad);
            Assert.Equal("https://maps.google.com/?q=18.9,73.3", vm.MapUrl);
            Assert.Equal("Ramesh", vm.CaretakerName);
            Assert.Equal("9876543210", vm.CaretakerPhone);
            Assert.Equal(2, vm.MinStayNights);
            Assert.Equal(14, vm.MaxStayNights);
            Assert.Equal(12500m, vm.PricePerNight);
            Assert.Equal(20, vm.MaxGuests);
            Assert.Equal(72, vm.FreeCancellationWindowHours);
            Assert.Equal(24, vm.CancellationCutoffHours);
            Assert.Equal("fixed", vm.CancellationFeeType);
            Assert.Equal(1500m, vm.CancellationFeeValue);
            Assert.Equal("/uploads/media/photo1.jpg", vm.ImageUrlInput);
            Assert.Equal(new[] { 1, 3 }, vm.SelectedAmenityIDs);
            Assert.Equal(new[] { 2, 4 }, vm.SelectedOccasionIDs);
            Assert.Equal(2, vm.ExistingMedia.Count);
        }

        [Fact]
        public async Task EditProperty_Post_AllowsOlderRecordWithBlankOptionalFields()
        {
            var olderProp = new Property
            {
                PropertyID = 43,
                OwnerID = 5,
                PropertyTypeID = 1,
                Title = "Legacy Villa",
                Location = "Lonavala",
                PricePerNight = 8000m,
                MaxGuests = 10,
                MinStayNights = 1,
                MaxStayNights = 30
            };

            var stub = StubPropRepo.Create(olderProp);
            var controller = CreateController((IPropertyRepository)(object)stub);

            var vm = new PropertyEditViewModel
            {
                PropertyID = 43,
                OwnerID = 5,
                PropertyTypeID = 1,
                Title = "Legacy Villa Updated",
                Location = "Lonavala",
                PricePerNight = 8500m,
                MaxGuests = 10,
                MinStayNights = 1,
                MaxStayNights = 30
            };

            var res = await controller.EditProperty(vm);
            Assert.True(controller.ModelState.IsValid);
            Assert.IsType<RedirectToActionResult>(res);
            Assert.True(stub.UpdatedCalled);
        }
    }
}
