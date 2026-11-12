using CsvHelper.Configuration.Attributes;

namespace AirQualityDW.Models;

/// <summary>
/// Bản ghi staging từ file CSV năm 2020 (aqi_airqualitydata_2020_en.csv)
/// Tất cả trường được đọc dưới dạng chuỗi để tránh lỗi khi dữ liệu không hợp lệ
/// </summary>
public class Staging2020Record
{
    /// <summary>Ngày đo (định dạng dd/MM/yyyy)</summary>
    [Name("Date")]
    public string? Date { get; set; }

    /// <summary>Mã quốc gia (VN)</summary>
    [Name("Country")]
    public string? Country { get; set; }

    /// <summary>Tên thành phố</summary>
    [Name("City")]
    public string? City { get; set; }

    /// <summary>Loại chất ô nhiễm hoặc thông số đo</summary>
    [Name("Specie")]
    public string? Specie { get; set; }

    /// <summary>Số lần đo trong ngày</summary>
    [Name("count")]
    public string? Count { get; set; }

    /// <summary>Giá trị nhỏ nhất</summary>
    [Name("min")]
    public string? Min { get; set; }

    /// <summary>Giá trị lớn nhất</summary>
    [Name("max")]
    public string? Max { get; set; }

    /// <summary>Giá trị trung vị</summary>
    [Name("median")]
    public string? Median { get; set; }

    /// <summary>Phương sai</summary>
    [Name("variance")]
    public string? Variance { get; set; }
}
