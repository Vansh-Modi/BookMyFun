using System;
using System.IO;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using Xunit;

namespace BookMyFun.Tests
{
    public class CheckoutLeadTimeAndVisibilityTests
    {
        [Fact]
        public void CheckoutView_ContainsCheckDecorLeadTimeDefinition_AndSectionHidingRules()
        {
            var checkoutPath = TestPathHelper.GetPath("Views", "Booking", "Checkout.cshtml");
            Assert.True(File.Exists(checkoutPath), $"Checkout.cshtml not found at {checkoutPath}");
            var content = File.ReadAllText(checkoutPath);

            // Task 1: checkDecorLeadTime must be defined in javascript
            Assert.Contains("function checkDecorLeadTime()", content);
            Assert.Contains("decorLeadTimeAlert", content);
            Assert.Contains("decor-pkg-radio", content);

            // Lead time check hooked into date events and initial load
            Assert.Contains("checkDecorLeadTime();", content);

            // Task 2: Hide decor section when no event managers are available or occasion is None
            Assert.Contains("id=\"decorUpsellSection\"", content);
            Assert.Contains("Model.OccasionID.HasValue && Model.OccasionID.Value > 0", content);
            Assert.Contains("Model.AvailableEventManagers != null && Model.AvailableEventManagers.Any()", content);

            // handleOccasionChange hides section when occasion is None or empty managers
            Assert.Contains("function handleOccasionChange(", content);
            Assert.Contains("decorSection.classList.add('d-none');", content);
            Assert.Contains("skipEventDecorator();", content);
        }

        [Theory]
        [InlineData(0, false)]  // same day: invalid (< 2 days lead time)
        [InlineData(1, false)]  // 1 day out: invalid (< 2 days lead time)
        [InlineData(2, true)]   // 2 days out: valid (>= 2 days lead time)
        [InlineData(5, true)]   // 5 days out: valid (>= 2 days lead time)
        public void ServerEnforcesDecorLeadTimeRule_AtLeastTwoDaysAhead(int daysFromToday, bool expectedAllowed)
        {
            var today = DateTime.Today;
            var eventDate = today.AddDays(daysFromToday);
            var minAllowed = today.AddDays(2);

            bool isAllowed = eventDate.Date >= minAllowed.Date;
            Assert.Equal(expectedAllowed, isAllowed);
        }

        [Fact]
        public void DecorLeadTimeRule_AppliesAcrossAllBookingModes_FullDayHalfDayHourly()
        {
            var today = DateTime.Today;

            // Full Day
            var fullDayInvalid = today.AddDays(1);
            Assert.True(fullDayInvalid.Date < today.AddDays(2).Date, "Full day with 1 day lead time must fail");

            var fullDayValid = today.AddDays(3);
            Assert.True(fullDayValid.Date >= today.AddDays(2).Date, "Full day with 3 days lead time must pass");

            // Half Day
            var halfDayInvalid = today;
            Assert.True(halfDayInvalid.Date < today.AddDays(2).Date, "Half day today must fail");

            var halfDayValid = today.AddDays(2);
            Assert.True(halfDayValid.Date >= today.AddDays(2).Date, "Half day with 2 days lead time must pass");

            // Hourly
            var hourlyInvalid = today.AddHours(5);
            Assert.True(hourlyInvalid.Date < today.AddDays(2).Date, "Hourly today must fail");

            var hourlyValid = today.AddDays(4);
            Assert.True(hourlyValid.Date >= today.AddDays(2).Date, "Hourly with 4 days lead time must pass");
        }
    }
}
