using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using BookMyFun.Models.ViewModels;

namespace BookMyFun.Models.Validation
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class RequiredDocumentFileAttribute : ValidationAttribute, IClientModelValidator
    {
        public static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".pdf" };
        public static readonly string[] AllowedContentTypes = { "image/jpeg", "image/jpg", "image/png", "application/pdf" };
        public const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

        public void AddValidation(ClientModelValidationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            MergeAttribute(context.Attributes, "data-val", "true");
            MergeAttribute(context.Attributes, "data-val-required", "Please upload a copy of your document.");
        }

        private static bool MergeAttribute(IDictionary<string, string> attributes, string key, string value)
        {
            if (attributes.ContainsKey(key)) return false;
            attributes.Add(key, value);
            return true;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            var model = validationContext.ObjectInstance as BookingCheckoutViewModel;
            var file = value as IFormFile;

            // Check if document submission is being made
            bool isDocumentSubmitted = model != null &&
                (!string.IsNullOrWhiteSpace(model.IdDocumentNumber) ||
                 !string.IsNullOrWhiteSpace(model.DocumentType));

            // If the model is BookingCheckoutViewModel, check if existing document is on file and unchanged
            bool hasExistingDoc = false;
            bool docNumberChanged = false;
            if (model != null)
            {
                hasExistingDoc = !string.IsNullOrWhiteSpace(model.ExistingDocumentKey) || model.IsClientAlreadyVerified;
                docNumberChanged = !string.IsNullOrWhiteSpace(model.ExistingDocumentNumber) &&
                    !IsSameDocumentNumber(model.IdDocumentNumber, model.ExistingDocumentNumber);
            }

            bool fileRequired = model != null
                ? (isDocumentSubmitted && (!hasExistingDoc || docNumberChanged))
                : false;

            if (fileRequired && (file == null || file.Length == 0))
            {
                return new ValidationResult("Please upload a copy of your document.", new[] { validationContext.MemberName ?? "IdDocumentFile" });
            }

            if (file != null && file.Length > 0)
            {
                if (file.Length > MaxFileSizeBytes)
                {
                    return new ValidationResult("Document exceeds maximum permitted size of 5MB.", new[] { validationContext.MemberName ?? "IdDocumentFile" });
                }

                string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
                {
                    return new ValidationResult("Only JPG, JPEG, PNG, and PDF files are accepted.", new[] { validationContext.MemberName ?? "IdDocumentFile" });
                }

                if (string.IsNullOrWhiteSpace(file.ContentType) || !AllowedContentTypes.Contains(file.ContentType.ToLowerInvariant()))
                {
                    return new ValidationResult("Invalid file content type. Please upload a valid image or PDF document.", new[] { validationContext.MemberName ?? "IdDocumentFile" });
                }
            }

            return ValidationResult.Success;
        }

        public static bool IsSameDocumentNumber(string? current, string? existing)
        {
            if (string.IsNullOrWhiteSpace(current) && string.IsNullOrWhiteSpace(existing)) return true;
            if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(existing)) return false;

            string cleanCur = current.Trim().Replace(" ", "").Replace("-", "");
            string cleanEx = existing.Trim().Replace(" ", "").Replace("-", "");

            if (string.Equals(cleanCur, cleanEx, StringComparison.OrdinalIgnoreCase)) return true;

            if (cleanEx.Contains('•') || cleanEx.Contains('*'))
            {
                string last4Ex = cleanEx.Length >= 4 ? cleanEx[^4..] : cleanEx;
                string last4Cur = cleanCur.Length >= 4 ? cleanCur[^4..] : cleanCur;
                return string.Equals(last4Cur, last4Ex, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
    }
}
