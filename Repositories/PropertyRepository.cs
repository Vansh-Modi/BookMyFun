using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public interface IPropertyRepository
    {
        Task<Property?> GetByIdAsync(int propertyId);
        Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false);
        Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId);
        Task<List<Property>> SearchPropertiesAsync(string? location, int? propertyTypeId, int? occasionId, decimal? minPrice, decimal? maxPrice, int? guests);
        Task<int> CreatePropertyAsync(Property property);
        Task<bool> UpdatePropertyAsync(Property property);
        Task<bool> DeletePropertyAsync(int propertyId);
        Task<bool> ApprovePropertyAsync(int propertyId, bool isApproved);
        Task<bool> SetPropertyOccasionsAsync(int propertyId, List<int> occasionIds);
        Task<List<int>> GetPropertyOccasionIdsAsync(int propertyId);
        Task<int> AddPropertyImageAsync(int propertyId, string imageUrl, bool isPrimary);
        Task<List<PropertyImage>> GetPropertyImagesAsync(int propertyId);
        Task<int> AddPropertyMediaAsync(PropertyMedia media);
        Task<List<PropertyMedia>> GetPropertyMediaAsync(int propertyId);
        Task<bool> DeletePropertyMediaAsync(int mediaId);
        Task<List<AmenityMaster>> GetAllAmenitiesAsync();
        Task<List<int>> GetPropertyAmenityIdsAsync(int propertyId);
        Task<List<AmenityMaster>> GetPropertyAmenitiesAsync(int propertyId);
        Task<bool> SetPropertyAmenitiesAsync(int propertyId, List<int> amenityIds);
        Task<List<PropertyType>> GetPropertyTypesAsync();
        Task<List<Occasion>> GetOccasionsAsync();
    }

    public class PropertyRepository : IPropertyRepository
    {
        private readonly DBHelper _db;

        public PropertyRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<Property?> GetByIdAsync(int propertyId)
        {
            const string sql = @"
                SELECT p.*, pt.TypeName AS PropertyTypeName, u.FullName AS OwnerName,
                       (SELECT TOP 1 ImageURL FROM PropertyImages WHERE PropertyID = p.PropertyID AND IsPrimary = 1) AS PrimaryImageUrl
                FROM Properties p
                JOIN PropertyTypes pt ON p.PropertyTypeID = pt.PropertyTypeID
                JOIN Users u ON p.OwnerID = u.UserID
                WHERE p.PropertyID = @PropertyID";

            var prop = await _db.ExecuteReaderSingleAsync(sql, MapProperty, new SqlParameter("@PropertyID", propertyId));
            if (prop != null)
            {
                prop.OccasionIDs = await GetPropertyOccasionIdsAsync(propertyId);
                var images = await GetPropertyImagesAsync(propertyId);
                prop.ImageUrls = images.Select(i => i.ImageURL).ToList();
                prop.MediaList = await GetPropertyMediaAsync(propertyId);
                prop.AmenityIDs = await GetPropertyAmenityIdsAsync(propertyId);
                prop.Amenities = await GetPropertyAmenitiesAsync(propertyId);

                if ((string.IsNullOrEmpty(prop.PrimaryImageUrl) || prop.PrimaryImageUrl.Contains("placeholder")) && prop.MediaList.Any())
                {
                    var primary = prop.MediaList.FirstOrDefault(m => m.IsPrimary && m.MediaType == "photo") ?? prop.MediaList.FirstOrDefault(m => m.MediaType == "photo");
                    if (primary != null)
                    {
                        prop.PrimaryImageUrl = primary.StorageUrl;
                    }
                }
            }
            return prop;
        }

        public async Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false)
        {
            var sql = @"
                SELECT p.*, pt.TypeName AS PropertyTypeName, u.FullName AS OwnerName,
                       (SELECT TOP 1 ImageURL FROM PropertyImages WHERE PropertyID = p.PropertyID ORDER BY IsPrimary DESC, ImageID ASC) AS PrimaryImageUrl
                FROM Properties p
                JOIN PropertyTypes pt ON p.PropertyTypeID = pt.PropertyTypeID
                JOIN Users u ON p.OwnerID = u.UserID
                WHERE 1=1 " + (onlyApproved ? " AND p.IsApproved = 1 AND p.ListingStatus = 'Active'" : "") +
                " ORDER BY p.CreatedAt DESC";

            return await _db.ExecuteReaderListAsync(sql, MapProperty);
        }

        public async Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId)
        {
            const string sql = @"
                SELECT p.*, pt.TypeName AS PropertyTypeName, u.FullName AS OwnerName,
                       (SELECT TOP 1 ImageURL FROM PropertyImages WHERE PropertyID = p.PropertyID ORDER BY IsPrimary DESC, ImageID ASC) AS PrimaryImageUrl
                FROM Properties p
                JOIN PropertyTypes pt ON p.PropertyTypeID = pt.PropertyTypeID
                JOIN Users u ON p.OwnerID = u.UserID
                WHERE p.OwnerID = @OwnerID
                ORDER BY p.CreatedAt DESC";

            return await _db.ExecuteReaderListAsync(sql, MapProperty, new SqlParameter("@OwnerID", ownerId));
        }

        public async Task<List<Property>> SearchPropertiesAsync(string? location, int? propertyTypeId, int? occasionId, decimal? minPrice, decimal? maxPrice, int? guests)
        {
            var sql = @"
                SELECT DISTINCT p.*, pt.TypeName AS PropertyTypeName, u.FullName AS OwnerName,
                       (SELECT TOP 1 ImageURL FROM PropertyImages WHERE PropertyID = p.PropertyID ORDER BY IsPrimary DESC, ImageID ASC) AS PrimaryImageUrl
                FROM Properties p
                JOIN PropertyTypes pt ON p.PropertyTypeID = pt.PropertyTypeID
                JOIN Users u ON p.OwnerID = u.UserID
                LEFT JOIN PropertyOccasions po ON p.PropertyID = po.PropertyID
                WHERE p.IsApproved = 1 AND p.ListingStatus = 'Active' ";

            var parameters = new List<SqlParameter>();

            if (!string.IsNullOrWhiteSpace(location))
            {
                sql += " AND (p.Location LIKE @Location OR p.Title LIKE @Location OR p.FormattedAddress LIKE @Location) ";
                parameters.Add(new SqlParameter("@Location", $"%{location}%"));
            }

            if (propertyTypeId.HasValue && propertyTypeId.Value > 0)
            {
                sql += " AND p.PropertyTypeID = @PropertyTypeID ";
                parameters.Add(new SqlParameter("@PropertyTypeID", propertyTypeId.Value));
            }

            if (occasionId.HasValue && occasionId.Value > 0)
            {
                sql += " AND po.OccasionID = @OccasionID ";
                parameters.Add(new SqlParameter("@OccasionID", occasionId.Value));
            }

            if (minPrice.HasValue)
            {
                sql += " AND p.PricePerNight >= @MinPrice ";
                parameters.Add(new SqlParameter("@MinPrice", minPrice.Value));
            }

            if (maxPrice.HasValue)
            {
                sql += " AND p.PricePerNight <= @MaxPrice ";
                parameters.Add(new SqlParameter("@MaxPrice", maxPrice.Value));
            }

            if (guests.HasValue)
            {
                sql += " AND p.MaxGuests >= @Guests ";
                parameters.Add(new SqlParameter("@Guests", guests.Value));
            }

            sql += " ORDER BY p.CreatedAt DESC";

            return await _db.ExecuteReaderListAsync(sql, MapProperty, parameters.ToArray());
        }

        public async Task<int> CreatePropertyAsync(Property property)
        {
            const string sql = @"
                INSERT INTO Properties 
                (OwnerID, PropertyTypeID, Title, Description, Location, FormattedAddress, Latitude, Longitude,
                 PricePerNight, SecurityDepositAmount, CommissionRate, FreeCancellationWindowHours, CancellationCutoffHours,
                 CancellationFeeType, CancellationFeeValue, MaxGuests, ListingStatus, IsApproved, IsFeatured, CreatedAt)
                OUTPUT INSERTED.PropertyID
                VALUES 
                (@OwnerID, @PropertyTypeID, @Title, @Description, @Location, @FormattedAddress, @Latitude, @Longitude,
                 @PricePerNight, @SecurityDepositAmount, @CommissionRate, @FreeCancellationWindowHours, @CancellationCutoffHours,
                 @CancellationFeeType, @CancellationFeeValue, @MaxGuests, @ListingStatus, @IsApproved, @IsFeatured, GETDATE())";

            var result = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@OwnerID", property.OwnerID),
                new SqlParameter("@PropertyTypeID", property.PropertyTypeID),
                new SqlParameter("@Title", property.Title),
                new SqlParameter("@Description", DBHelper.ToDbValue(property.Description)),
                new SqlParameter("@Location", property.Location),
                new SqlParameter("@FormattedAddress", DBHelper.ToDbValue(property.FormattedAddress)),
                new SqlParameter("@Latitude", DBHelper.ToDbValue(property.Latitude)),
                new SqlParameter("@Longitude", DBHelper.ToDbValue(property.Longitude)),
                new SqlParameter("@PricePerNight", property.PricePerNight),
                new SqlParameter("@SecurityDepositAmount", property.SecurityDepositAmount),
                new SqlParameter("@CommissionRate", DBHelper.ToDbValue(property.CommissionRate)),
                new SqlParameter("@FreeCancellationWindowHours", property.FreeCancellationWindowHours),
                new SqlParameter("@CancellationCutoffHours", property.CancellationCutoffHours),
                new SqlParameter("@CancellationFeeType", property.CancellationFeeType ?? "percentage"),
                new SqlParameter("@CancellationFeeValue", property.CancellationFeeValue),
                new SqlParameter("@MaxGuests", property.MaxGuests),
                new SqlParameter("@ListingStatus", property.ListingStatus),
                new SqlParameter("@IsApproved", property.IsApproved),
                new SqlParameter("@IsFeatured", property.IsFeatured));

            var propertyId = result != null ? Convert.ToInt32(result) : 0;

            if (propertyId > 0)
            {
                if (property.OccasionIDs != null && property.OccasionIDs.Any())
                {
                    await SetPropertyOccasionsAsync(propertyId, property.OccasionIDs);
                }
                if (property.AmenityIDs != null && property.AmenityIDs.Any())
                {
                    await SetPropertyAmenitiesAsync(propertyId, property.AmenityIDs);
                }
            }

            return propertyId;
        }

        public async Task<bool> UpdatePropertyAsync(Property property)
        {
            const string sql = @"
                UPDATE Properties 
                SET PropertyTypeID = @PropertyTypeID,
                    Title = @Title,
                    Description = @Description,
                    Location = @Location,
                    FormattedAddress = @FormattedAddress,
                    Latitude = @Latitude,
                    Longitude = @Longitude,
                    PricePerNight = @PricePerNight,
                    SecurityDepositAmount = @SecurityDepositAmount,
                    CommissionRate = @CommissionRate,
                    FreeCancellationWindowHours = @FreeCancellationWindowHours,
                    CancellationCutoffHours = @CancellationCutoffHours,
                    CancellationFeeType = @CancellationFeeType,
                    CancellationFeeValue = @CancellationFeeValue,
                    MaxGuests = @MaxGuests,
                    ListingStatus = @ListingStatus,
                    IsFeatured = @IsFeatured
                WHERE PropertyID = @PropertyID";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@PropertyTypeID", property.PropertyTypeID),
                new SqlParameter("@Title", property.Title),
                new SqlParameter("@Description", DBHelper.ToDbValue(property.Description)),
                new SqlParameter("@Location", property.Location),
                new SqlParameter("@FormattedAddress", DBHelper.ToDbValue(property.FormattedAddress)),
                new SqlParameter("@Latitude", DBHelper.ToDbValue(property.Latitude)),
                new SqlParameter("@Longitude", DBHelper.ToDbValue(property.Longitude)),
                new SqlParameter("@PricePerNight", property.PricePerNight),
                new SqlParameter("@SecurityDepositAmount", property.SecurityDepositAmount),
                new SqlParameter("@CommissionRate", DBHelper.ToDbValue(property.CommissionRate)),
                new SqlParameter("@FreeCancellationWindowHours", property.FreeCancellationWindowHours),
                new SqlParameter("@CancellationCutoffHours", property.CancellationCutoffHours),
                new SqlParameter("@CancellationFeeType", property.CancellationFeeType ?? "percentage"),
                new SqlParameter("@CancellationFeeValue", property.CancellationFeeValue),
                new SqlParameter("@MaxGuests", property.MaxGuests),
                new SqlParameter("@ListingStatus", property.ListingStatus),
                new SqlParameter("@IsFeatured", property.IsFeatured),
                new SqlParameter("@PropertyID", property.PropertyID));

            if (property.OccasionIDs != null)
            {
                await SetPropertyOccasionsAsync(property.PropertyID, property.OccasionIDs);
            }
            if (property.AmenityIDs != null)
            {
                await SetPropertyAmenitiesAsync(property.PropertyID, property.AmenityIDs);
            }

            return rows > 0;
        }

        public async Task<bool> DeletePropertyAsync(int propertyId)
        {
            const string sql = "DELETE FROM Properties WHERE PropertyID = @PropertyID";
            var rows = await _db.ExecuteNonQueryAsync(sql, new SqlParameter("@PropertyID", propertyId));
            return rows > 0;
        }

        public async Task<bool> ApprovePropertyAsync(int propertyId, bool isApproved)
        {
            const string sql = @"
                UPDATE Properties 
                SET IsApproved = @IsApproved,
                    ListingStatus = CASE WHEN @IsApproved = 1 THEN 'Active' ELSE 'Draft' END
                WHERE PropertyID = @PropertyID";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@IsApproved", isApproved),
                new SqlParameter("@PropertyID", propertyId));

            return rows > 0;
        }

        public async Task<bool> SetPropertyOccasionsAsync(int propertyId, List<int> occasionIds)
        {
            await _db.ExecuteNonQueryAsync("DELETE FROM PropertyOccasions WHERE PropertyID = @PropertyID",
                new SqlParameter("@PropertyID", propertyId));

            foreach (var occId in occasionIds)
            {
                await _db.ExecuteNonQueryAsync("INSERT INTO PropertyOccasions (PropertyID, OccasionID) VALUES (@PropertyID, @OccasionID)",
                    new SqlParameter("@PropertyID", propertyId),
                    new SqlParameter("@OccasionID", occId));
            }
            return true;
        }

        public async Task<List<int>> GetPropertyOccasionIdsAsync(int propertyId)
        {
            const string sql = "SELECT OccasionID FROM PropertyOccasions WHERE PropertyID = @PropertyID";
            return await _db.ExecuteReaderListAsync(sql, r => r.GetInt32(0), new SqlParameter("@PropertyID", propertyId));
        }

        public async Task<int> AddPropertyImageAsync(int propertyId, string imageUrl, bool isPrimary)
        {
            if (isPrimary)
            {
                await _db.ExecuteNonQueryAsync("UPDATE PropertyImages SET IsPrimary = 0 WHERE PropertyID = @PropertyID",
                    new SqlParameter("@PropertyID", propertyId));
            }

            const string sql = @"
                INSERT INTO PropertyImages (PropertyID, ImageURL, IsPrimary)
                OUTPUT INSERTED.ImageID
                VALUES (@PropertyID, @ImageURL, @IsPrimary)";

            var result = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@PropertyID", propertyId),
                new SqlParameter("@ImageURL", imageUrl),
                new SqlParameter("@IsPrimary", isPrimary));

            return result != null ? Convert.ToInt32(result) : 0;
        }

        public async Task<List<PropertyImage>> GetPropertyImagesAsync(int propertyId)
        {
            const string sql = "SELECT * FROM PropertyImages WHERE PropertyID = @PropertyID ORDER BY IsPrimary DESC, ImageID ASC";
            return await _db.ExecuteReaderListAsync(sql, r => new PropertyImage
            {
                ImageID = r.GetInt32(r.GetOrdinal("ImageID")),
                PropertyID = r.GetInt32(r.GetOrdinal("PropertyID")),
                ImageURL = r.GetString(r.GetOrdinal("ImageURL")),
                IsPrimary = r.GetBoolean(r.GetOrdinal("IsPrimary"))
            }, new SqlParameter("@PropertyID", propertyId));
        }

        public async Task<int> AddPropertyMediaAsync(PropertyMedia media)
        {
            if (media.IsPrimary)
            {
                await _db.ExecuteNonQueryAsync("UPDATE PropertyMedia SET IsPrimary = 0 WHERE PropertyID = @PropertyID",
                    new SqlParameter("@PropertyID", media.PropertyID));
            }

            const string sql = @"
                INSERT INTO PropertyMedia (PropertyID, MediaType, SourceType, StorageUrl, PublicId, IsPrimary, SortOrder, CreatedAt)
                OUTPUT INSERTED.MediaID
                VALUES (@PropertyID, @MediaType, @SourceType, @StorageUrl, @PublicId, @IsPrimary, @SortOrder, GETDATE())";

            var result = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@PropertyID", media.PropertyID),
                new SqlParameter("@MediaType", media.MediaType),
                new SqlParameter("@SourceType", media.SourceType),
                new SqlParameter("@StorageUrl", media.StorageUrl),
                new SqlParameter("@PublicId", DBHelper.ToDbValue(media.PublicId)),
                new SqlParameter("@IsPrimary", media.IsPrimary),
                new SqlParameter("@SortOrder", media.SortOrder));

            return result != null ? Convert.ToInt32(result) : 0;
        }

        public async Task<List<PropertyMedia>> GetPropertyMediaAsync(int propertyId)
        {
            const string sql = "SELECT * FROM PropertyMedia WHERE PropertyID = @PropertyID ORDER BY IsPrimary DESC, SortOrder ASC, MediaID ASC";
            return await _db.ExecuteReaderListAsync(sql, r => new PropertyMedia
            {
                MediaID = r.GetInt32(r.GetOrdinal("MediaID")),
                PropertyID = r.GetInt32(r.GetOrdinal("PropertyID")),
                MediaType = r.GetString(r.GetOrdinal("MediaType")),
                SourceType = r.GetString(r.GetOrdinal("SourceType")),
                StorageUrl = r.GetString(r.GetOrdinal("StorageUrl")),
                PublicId = r.IsDBNull(r.GetOrdinal("PublicId")) ? null : r.GetString(r.GetOrdinal("PublicId")),
                IsPrimary = r.GetBoolean(r.GetOrdinal("IsPrimary")),
                SortOrder = r.GetInt32(r.GetOrdinal("SortOrder")),
                CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt"))
            }, new SqlParameter("@PropertyID", propertyId));
        }

        public async Task<bool> DeletePropertyMediaAsync(int mediaId)
        {
            const string sql = "DELETE FROM PropertyMedia WHERE MediaID = @MediaID";
            var rows = await _db.ExecuteNonQueryAsync(sql, new SqlParameter("@MediaID", mediaId));
            return rows > 0;
        }

        public async Task<List<AmenityMaster>> GetAllAmenitiesAsync()
        {
            const string sql = "SELECT * FROM AmenitiesMaster ORDER BY Category, AmenityName";
            return await _db.ExecuteReaderListAsync(sql, r => new AmenityMaster
            {
                AmenityID = r.GetInt32(r.GetOrdinal("AmenityID")),
                AmenityName = r.GetString(r.GetOrdinal("AmenityName")),
                IconClass = r.GetString(r.GetOrdinal("IconClass")),
                Category = r.GetString(r.GetOrdinal("Category"))
            });
        }

        public async Task<List<int>> GetPropertyAmenityIdsAsync(int propertyId)
        {
            const string sql = "SELECT AmenityID FROM PropertyAmenitiesMapping WHERE PropertyID = @PropertyID";
            return await _db.ExecuteReaderListAsync(sql, r => r.GetInt32(0), new SqlParameter("@PropertyID", propertyId));
        }

        public async Task<List<AmenityMaster>> GetPropertyAmenitiesAsync(int propertyId)
        {
            const string sql = @"
                SELECT a.*
                FROM AmenitiesMaster a
                JOIN PropertyAmenitiesMapping pam ON a.AmenityID = pam.AmenityID
                WHERE pam.PropertyID = @PropertyID
                ORDER BY a.Category, a.AmenityName";

            return await _db.ExecuteReaderListAsync(sql, r => new AmenityMaster
            {
                AmenityID = r.GetInt32(r.GetOrdinal("AmenityID")),
                AmenityName = r.GetString(r.GetOrdinal("AmenityName")),
                IconClass = r.GetString(r.GetOrdinal("IconClass")),
                Category = r.GetString(r.GetOrdinal("Category"))
            }, new SqlParameter("@PropertyID", propertyId));
        }

        public async Task<bool> SetPropertyAmenitiesAsync(int propertyId, List<int> amenityIds)
        {
            await _db.ExecuteNonQueryAsync("DELETE FROM PropertyAmenitiesMapping WHERE PropertyID = @PropertyID",
                new SqlParameter("@PropertyID", propertyId));

            foreach (var amId in amenityIds)
            {
                await _db.ExecuteNonQueryAsync("INSERT INTO PropertyAmenitiesMapping (PropertyID, AmenityID) VALUES (@PropertyID, @AmenityID)",
                    new SqlParameter("@PropertyID", propertyId),
                    new SqlParameter("@AmenityID", amId));
            }
            return true;
        }

        public async Task<List<PropertyType>> GetPropertyTypesAsync()
        {
            const string sql = "SELECT * FROM PropertyTypes ORDER BY TypeName";
            return await _db.ExecuteReaderListAsync(sql, r => new PropertyType
            {
                PropertyTypeID = r.GetInt32(r.GetOrdinal("PropertyTypeID")),
                TypeName = r.GetString(r.GetOrdinal("TypeName")),
                Description = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description"))
            });
        }

        public async Task<List<Occasion>> GetOccasionsAsync()
        {
            const string sql = "SELECT * FROM Occasions ORDER BY OccasionName";
            return await _db.ExecuteReaderListAsync(sql, r => new Occasion
            {
                OccasionID = r.GetInt32(r.GetOrdinal("OccasionID")),
                OccasionName = r.GetString(r.GetOrdinal("OccasionName")),
                Description = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description"))
            });
        }

        private static Property MapProperty(SqlDataReader reader)
        {
            var p = new Property
            {
                PropertyID = reader.GetInt32(reader.GetOrdinal("PropertyID")),
                OwnerID = reader.GetInt32(reader.GetOrdinal("OwnerID")),
                PropertyTypeID = reader.GetInt32(reader.GetOrdinal("PropertyTypeID")),
                Title = reader.GetString(reader.GetOrdinal("Title")),
                Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                Location = reader.GetString(reader.GetOrdinal("Location")),
                PricePerNight = reader.GetDecimal(reader.GetOrdinal("PricePerNight")),
                MaxGuests = reader.GetInt32(reader.GetOrdinal("MaxGuests")),
                ListingStatus = reader.GetString(reader.GetOrdinal("ListingStatus")),
                IsApproved = reader.GetBoolean(reader.GetOrdinal("IsApproved")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
            };

            try
            {
                int addrOrd = reader.GetOrdinal("FormattedAddress");
                if (!reader.IsDBNull(addrOrd)) p.FormattedAddress = reader.GetString(addrOrd);
            }
            catch { }

            try
            {
                int latOrd = reader.GetOrdinal("Latitude");
                if (!reader.IsDBNull(latOrd)) p.Latitude = reader.GetDecimal(latOrd);
            }
            catch { }

            try
            {
                int lngOrd = reader.GetOrdinal("Longitude");
                if (!reader.IsDBNull(lngOrd)) p.Longitude = reader.GetDecimal(lngOrd);
            }
            catch { }

            try
            {
                int secDepOrd = reader.GetOrdinal("SecurityDepositAmount");
                if (!reader.IsDBNull(secDepOrd)) p.SecurityDepositAmount = reader.GetDecimal(secDepOrd);
            }
            catch { }

            try
            {
                int commOrd = reader.GetOrdinal("CommissionRate");
                if (!reader.IsDBNull(commOrd)) p.CommissionRate = reader.GetDecimal(commOrd);
            }
            catch { }

            try
            {
                int freeWinOrd = reader.GetOrdinal("FreeCancellationWindowHours");
                if (!reader.IsDBNull(freeWinOrd)) p.FreeCancellationWindowHours = reader.GetInt32(freeWinOrd);
            }
            catch { }

            try
            {
                int cutoffOrd = reader.GetOrdinal("CancellationCutoffHours");
                if (!reader.IsDBNull(cutoffOrd)) p.CancellationCutoffHours = reader.GetInt32(cutoffOrd);
            }
            catch { }

            try
            {
                int feeTypeOrd = reader.GetOrdinal("CancellationFeeType");
                if (!reader.IsDBNull(feeTypeOrd)) p.CancellationFeeType = reader.GetString(feeTypeOrd);
            }
            catch { }

            try
            {
                int feeValOrd = reader.GetOrdinal("CancellationFeeValue");
                if (!reader.IsDBNull(feeValOrd)) p.CancellationFeeValue = reader.GetDecimal(feeValOrd);
            }
            catch { }

            try
            {
                int featOrd = reader.GetOrdinal("IsFeatured");
                if (!reader.IsDBNull(featOrd)) p.IsFeatured = reader.GetBoolean(featOrd);
            }
            catch { }

            try
            {
                p.PropertyTypeName = reader.GetString(reader.GetOrdinal("PropertyTypeName"));
            }
            catch { }

            try
            {
                p.OwnerName = reader.GetString(reader.GetOrdinal("OwnerName"));
            }
            catch { }

            try
            {
                int imgCol = reader.GetOrdinal("PrimaryImageUrl");
                p.PrimaryImageUrl = reader.IsDBNull(imgCol) ? "/images/property-placeholder.jpg" : reader.GetString(imgCol);
            }
            catch { }

            return p;
        }
    }
}
