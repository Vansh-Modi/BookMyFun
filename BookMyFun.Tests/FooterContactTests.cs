using System;
using System.IO;
using BookMyFun.Models.Entities;
using Xunit;

namespace BookMyFun.Tests
{
    public class FooterContactTests
    {
        [Fact]
        public void DeveloperCreditCard_MaintainsExistingLayout_AndIncludesContactFields()
        {
            var layoutPath = TestPathHelper.GetPath("Views", "Shared", "_Layout.cshtml");
            Assert.True(File.Exists(layoutPath), $"Expected _Layout.cshtml to exist at {layoutPath}");
            var content = File.ReadAllText(layoutPath);

            // Verify developer credit card container exists and keeps the pill layout
            Assert.Contains("developer-credit", content);
            Assert.Contains("Developed by <strong class=\"text-warning\">Vansh Modi</strong>", content);

            // Verify Mobile tel link: 8849077959 with target="_blank" rel="noopener noreferrer"
            Assert.Contains("href=\"tel:8849077959\"", content);
            Assert.Contains("target=\"_blank\"", content);
            Assert.Contains("rel=\"noopener noreferrer\"", content);

            // Verify Email mailto link: vanshmodi268@gmail.com
            Assert.Contains("href=\"mailto:vanshmodi268@gmail.com\"", content);

            // Verify existing GitHub and LinkedIn links are preserved
            Assert.Contains("href=\"https://github.com/Vansh-Modi\"", content);
            Assert.Contains("href=\"https://www.linkedin.com/in/vansh-modi-08041731b/?isSelfProfile=true\"", content);
        }

        [Fact]
        public void FooterContactDetails_ApplyHideIfEmptyRule()
        {
            var layoutPath = TestPathHelper.GetPath("Views", "Shared", "_Layout.cshtml");
            Assert.True(File.Exists(layoutPath), $"Expected _Layout.cshtml to exist at {layoutPath}");
            var content = File.ReadAllText(layoutPath);

            // Address: business address block completely hidden if empty
            Assert.Contains("@if (!string.IsNullOrWhiteSpace(site.Address))", content);

            // Support phone, whatsapp, email hidden if empty
            Assert.Contains("@if (!string.IsNullOrWhiteSpace(site.SupportPhone))", content);
            Assert.Contains("@if (!string.IsNullOrWhiteSpace(site.WhatsAppNumber))", content);
            Assert.Contains("@if (!string.IsNullOrWhiteSpace(site.SupportEmail))", content);

            // Social media links hidden if empty
            Assert.Contains("@if (!string.IsNullOrEmpty(site.InstagramUrl))", content);
            Assert.Contains("@if (!string.IsNullOrEmpty(site.FacebookUrl))", content);
            Assert.Contains("@if (!string.IsNullOrEmpty(site.YouTubeUrl))", content);
        }

        [Fact]
        public void ContactPageView_AppliesHideIfEmptyRule_ToDirectChannels()
        {
            var contactPath = TestPathHelper.GetPath("Views", "Home", "Contact.cshtml");
            Assert.True(File.Exists(contactPath), $"Expected Contact.cshtml to exist at {contactPath}");
            var content = File.ReadAllText(contactPath);

            Assert.Contains("@if (!string.IsNullOrWhiteSpace(site.Address))", content);
            Assert.Contains("@if (!string.IsNullOrWhiteSpace(site.SupportPhone))", content);
            Assert.Contains("@if (!string.IsNullOrWhiteSpace(site.WhatsAppNumber))", content);
            Assert.Contains("@if (!string.IsNullOrWhiteSpace(site.SupportEmail))", content);
        }
    }
}
