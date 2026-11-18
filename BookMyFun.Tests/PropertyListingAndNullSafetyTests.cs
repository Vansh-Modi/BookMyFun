using System;
using System.IO;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using Xunit;

namespace BookMyFun.Tests
{
    public class PropertyListingAndNullSafetyTests
    {
        [Fact]
        public void PropertyRepository_UsesLeftJoins_AndNullGuards_ToPreventCrashOnNullableData()
        {
            var repoPath = TestPathHelper.GetPath("Repositories", "PropertyRepository.cs");
            Assert.True(File.Exists(repoPath), $"PropertyRepository.cs not found at {repoPath}");
            var content = File.ReadAllText(repoPath);

            // Bug fix check 1: GetPropertiesByOwnerIdAsync uses LEFT JOIN instead of INNER JOIN
            Assert.Contains("LEFT JOIN PropertyTypes pt ON p.PropertyTypeID = pt.PropertyTypeID", content);
            Assert.Contains("LEFT JOIN Users u ON p.OwnerID = u.UserID", content);

            // Bug fix check 2: MapProperty has safe IsDBNull checks on all newly nullable/optional columns
            Assert.Contains("reader.IsDBNull(reader.GetOrdinal(\"Title\"))", content);
            Assert.Contains("reader.IsDBNull(reader.GetOrdinal(\"Location\"))", content);
            Assert.Contains("reader.IsDBNull(reader.GetOrdinal(\"ListingStatus\"))", content);
            Assert.Contains("reader.GetOrdinal(\"City\")", content);
            Assert.Contains("reader.GetOrdinal(\"State\")", content);
            Assert.Contains("reader.GetOrdinal(\"Pincode\")", content);
            Assert.Contains("reader.GetOrdinal(\"CaretakerName\")", content);
            Assert.Contains("reader.GetOrdinal(\"CaretakerPhone\")", content);
            Assert.Contains("reader.GetOrdinal(\"OwnerIsCaretaker\")", content);
            Assert.Contains("reader.GetOrdinal(\"MapUrlLocked\")", content);
            Assert.Contains("reader.GetOrdinal(\"EventCommissionRate\")", content);
        }

        [Fact]
        public void PropertyEditViewModel_ContainsAllRequiredAddressAndCaretakerProperties()
        {
            var vm = new PropertyEditViewModel
            {
                PropertyID = 1,
                Title = "Farm Stay",
                City = "Pune",
                State = "Maharashtra",
                Pincode = "411001",
                NearbyAreaOrRoad = "Near Old Highway",
                CaretakerName = "Ramesh",
                CaretakerPhone = "9876543210",
                OwnerIsCaretaker = true,
                MapUrlLocked = true,
                EventCommissionRate = 12.5m
            };

            Assert.Equal("Pune", vm.City);
            Assert.Equal("Maharashtra", vm.State);
            Assert.Equal("411001", vm.Pincode);
            Assert.Equal("Near Old Highway", vm.NearbyAreaOrRoad);
            Assert.Equal("Ramesh", vm.CaretakerName);
            Assert.Equal("9876543210", vm.CaretakerPhone);
            Assert.True(vm.OwnerIsCaretaker);
            Assert.True(vm.MapUrlLocked);
            Assert.Equal(12.5m, vm.EventCommissionRate);
        }

        [Fact]
        public void PropertyEntity_AllowsNullsInNewColumns_WithoutExceptions()
        {
            var prop = new Property
            {
                PropertyID = 1,
                OwnerID = 2,
                Title = null!, // simulates DBNull
                Location = null!, // simulates DBNull
                City = null,
                State = null,
                Pincode = null,
                CaretakerName = null,
                CaretakerPhone = null,
                OwnerIsCaretaker = false,
                MapUrlLocked = false,
                EventCommissionRate = null
            };

            Assert.Null(prop.City);
            Assert.Null(prop.State);
            Assert.Null(prop.Pincode);
            Assert.Null(prop.CaretakerName);
            Assert.Null(prop.CaretakerPhone);
            Assert.False(prop.OwnerIsCaretaker);
            Assert.False(prop.MapUrlLocked);
            Assert.Null(prop.EventCommissionRate);
        }

        [Fact]
        public void CreatePropertyAsync_And_UpdatePropertyAsync_Exclude_IsFeatured_And_SecurityDepositAmount()
        {
            var repoPath = TestPathHelper.GetPath("Repositories", "PropertyRepository.cs");
            var content = File.ReadAllText(repoPath);

            // In CreatePropertyAsync sql statement
            var createIdx = content.IndexOf("public async Task<int> CreatePropertyAsync");
            var createEnd = content.IndexOf("public async Task<bool> UpdatePropertyAsync", createIdx);
            var createSql = content.Substring(createIdx, createEnd - createIdx);

            Assert.DoesNotContain(", IsFeatured,", createSql);
            Assert.DoesNotContain("@IsFeatured", createSql);
            Assert.DoesNotContain(", SecurityDepositAmount,", createSql);
            Assert.DoesNotContain("@SecurityDepositAmount", createSql);
            Assert.Contains("MinStayNights", createSql);
            Assert.Contains("MaxStayNights", createSql);
            Assert.Contains("MapUrlLocked", createSql);

            // In UpdatePropertyAsync sql statement
            var updateEnd = content.IndexOf("public async Task<bool> UpdatePropertyMapUrlAsync", createEnd);
            var updateSql = content.Substring(createEnd, updateEnd - createEnd);

            Assert.DoesNotContain("IsFeatured =", updateSql);
            Assert.DoesNotContain("SecurityDepositAmount =", updateSql);
            Assert.Contains("MinStayNights =", updateSql);
            Assert.Contains("MaxStayNights =", updateSql);
        }

        [Fact]
        public void Views_Contain_StayLimits_ValidationSpans_And_SafeDetectLocation()
        {
            var createPath = TestPathHelper.GetPath("Views", "Owner", "CreateProperty.cshtml");
            var createContent = File.ReadAllText(createPath);

            Assert.Contains("asp-for=\"MinStayNights\"", createContent);
            Assert.Contains("asp-for=\"MaxStayNights\"", createContent);
            Assert.Contains("asp-validation-for=\"MinStayNights\"", createContent);
            Assert.Contains("asp-validation-for=\"MaxStayNights\"", createContent);
            Assert.Contains("asp-validation-for=\"Pincode\"", createContent);
            Assert.Contains("asp-validation-for=\"State\"", createContent);
            Assert.Contains("asp-validation-for=\"MapUrl\"", createContent);
            Assert.Contains("btnDetectLocation\")?.", createContent);
            Assert.Contains("id=\"btnTestMapUrl\"", createContent);

            var editPath = TestPathHelper.GetPath("Views", "Owner", "EditProperty.cshtml");
            var editContent = File.ReadAllText(editPath);

            Assert.Contains("asp-for=\"MinStayNights\"", editContent);
            Assert.Contains("asp-for=\"MaxStayNights\"", editContent);
            Assert.Contains("id=\"btnTestMapUrl\"", editContent);
            Assert.Contains("btnDetectLocation\")?.", editContent);
        }
    }
}
