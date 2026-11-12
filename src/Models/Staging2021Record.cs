using CsvHelper.Configuration.Attributes;

namespace AirQualityDW.Models;

/// <summary>
/// Bản ghi staging từ file CSV năm 2021 (historical_air_quality_2021_en.csv)
/// Tất cả trường được đọc dưới dạng chuỗi để tránh lỗi khi dữ liệu không hợp lệ
/// Lưu ý: trường Location có định dạng "lat,lon" (có dấu phẩy, cần xử lý đặc biệt)
/// </summary>
public class Staging2021Record
{
    /// <summary>Mã trạm đo</summary>
    [Name("Station ID")]
    public string? StationId { get; set; }

    /// <summary>Chỉ số AQI tổng hợp</summary>
    [Name("AQI index")]
    public string? AqiIndex { get; set; }

    /// <summary>Vị trí địa lý "vĩ độ,kinh độ" (ví dụ: "10.78,106.70")</summary>
    [Name("Location")]
    public string? Location { get; set; }

    /// <summary>Tên trạm đo</summary>
    [Name("Station name")]
    public string? StationName { get; set; }

    /// <summary>Đường dẫn URL đến trang AQICN</summary>
    [Name("Url")]
    public string? Url { get; set; }

    /// <summary>Chất ô nhiễm chính</summary>
    [Name("Dominent pollutant")]
    public string? DominentPollutant { get; set; }

    /// <summary>Nồng độ Carbon Monoxide (CO)</summary>
    [Name("CO")]
    public string? Co { get; set; }

    /// <summary>Điểm sương (Dew point)</summary>
    [Name("Dew")]
    public string? Dew { get; set; }

    /// <summary>Độ ẩm không khí (%)</summary>
    [Name("Humidity")]
    public string? Humidity { get; set; }

    /// <summary>Nồng độ Nitrogen Dioxide (NO2)</summary>
    [Name("NO2")]
    public string? No2 { get; set; }

    /// <summary>Nồng độ Ozone (O3)</summary>
    [Name("O3")]
    public string? O3 { get; set; }

    /// <summary>Áp suất khí quyển (hPa) - có thể dạng "1,009.00"</summary>
    [Name("Pressure")]
    public string? Pressure { get; set; }

    /// <summary>Nồng độ bụi PM10</summary>
    [Name("PM10")]
    public string? Pm10 { get; set; }

    /// <summary>Nồng độ bụi mịn PM2.5</summary>
    [Name("PM2.5")]
    public string? Pm25 { get; set; }

    /// <summary>Nồng độ Sulfur Dioxide (SO2)</summary>
    [Name("SO2")]
    public string? So2 { get; set; }

    /// <summary>Nhiệt độ không khí (°C)</summary>
    [Name("Temperature")]
    public string? Temperature { get; set; }

    /// <summary>Tốc độ gió (m/s)</summary>
    [Name("Wind")]
    public string? Wind { get; set; }

    /// <summary>Thời gian đo (định dạng yyyy-MM-dd HH:mm:ss)</summary>
    [Name("Data Time S")]
    public string? DataTimeS { get; set; }

    /// <summary>Múi giờ (ví dụ: +07:00)</summary>
    [Name("Data Time Tz")]
    public string? DataTimeTz { get; set; }

    /// <summary>Trạng thái dữ liệu</summary>
    [Name("Status")]
    public string? Status { get; set; }

    /// <summary>Mức cảnh báo</summary>
    [Name("Alert level")]
    public string? AlertLevel { get; set; }
}
