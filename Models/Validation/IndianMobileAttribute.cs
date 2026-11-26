using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace BookMyFun.Models.Validation
{
    public static class IndianMobileHelper
    {
        private static readonly Regex TenDigitIndianMobileRegex = new(@"^[6-9]\d{9}$", RegexOptions.Compiled);

        /// <summary>
        /// Strips whitespace, dashes, parentheses, and a leading +91/91/0 country or trunk prefix
        /// when present, returning the normalized 10-digit string (or cleaned digits/input if invalid).
        /// </summary>
        public static string NormalizeTo10Digits(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            string cleaned = Regex.Replace(input.Trim(), @"[\s\-\(\)]", "");

            if (cleaned.StartsWith("+91", StringComparison.Ordinal))
            {
                cleaned = cleaned.Substring(3);
            }
            else if (cleaned.StartsWith("91", StringComparison.Ordinal) && cleaned.Length == 12 && cleaned.All(char.IsDigit))
            {
                cleaned = cleaned.Substring(2);
            }
            else if (cleaned.StartsWith("0", StringComparison.Ordinal) && cleaned.Length == 11 && cleaned.All(char.IsDigit))
            {
                cleaned = cleaned.Substring(1);
            }

            return cleaned;
        }

        public static bool IsValidIndianMobile(string? input, out string normalized10)
        {
            normalized10 = NormalizeTo10Digits(input);
            return TenDigitIndianMobileRegex.IsMatch(normalized10);
        }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class IndianMobileAttribute : ValidationAttribute
    {
        public const string DefaultErrorMessage = "Please enter a valid 10-digit Indian mobile number starting with 6, 7, 8, or 9.";

        public IndianMobileAttribute() : base(DefaultErrorMessage) { }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            var str = value as string;
            if (string.IsNullOrWhiteSpace(str))
            {
                return ValidationResult.Success;
            }

            if (IndianMobileHelper.IsValidIndianMobile(str, out _))
            {
                return ValidationResult.Success;
            }

            var memberNames = string.IsNullOrEmpty(validationContext.MemberName)
                ? null
                : new[] { validationContext.MemberName };
            return new ValidationResult(ErrorMessage ?? DefaultErrorMessage, memberNames);
        }
    }
}
