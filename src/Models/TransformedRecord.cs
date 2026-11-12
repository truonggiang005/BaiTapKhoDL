namespace AirQualityDW.Models;

/// <summary>
/// Bản ghi đã được làm sạch và chuẩn hóa, sẵn sàng để nạp vào bảng fact
/// Được tạo ra từ quá trình Transform từ dữ liệu staging
/// </summary>
public class TransformedRecord
{
    /// <summary>Ngày đo (đã được parse từ chuỗi)</summary>
    public DateTime Date { get; set; }

    /// <summary>Tên thành phố (tiếng Việt có dấu)</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>Tên tỉnh/thành phố (tiếng Việt có dấu)</summary>
    public string Province { get; set; } = string.Empty;

    /// <summary>Mã trạm đo (null nếu từ dữ liệu 2020)</summary>
    public string? StationId { get; set; }

    /// <summary>Tên trạm đo</summary>
    public string? StationName { get; set; }

    /// <summary>Vĩ độ trạm đo (null nếu từ dữ liệu 2020)</summary>
    public double? Latitude { get; set; }

    /// <summary>Kinh độ trạm đo (null nếu từ dữ liệu 2020)</summary>
    public double? Longitude { get; set; }

    /// <summary>Mã chất ô nhiễm (aqi, pm25, pm10, no2, co, o3, so2, temperature, humidity, dew, pressure, wind)</summary>
    public string PollutantCode { get; set; } = string.Empty;

    /// <summary>Số lần đo</summary>
    public int MeasurementCount { get; set; }

    /// <summary>Giá trị nhỏ nhất</summary>
    public decimal? MinValue { get; set; }

    /// <summary>Giá trị lớn nhất</summary>
    public decimal? MaxValue { get; set; }

    /// <summary>Giá trị trung vị</summary>
    public decimal? MedianValue { get; set; }

    /// <summary>Giá trị trung bình</summary>
    public decimal? AvgValue { get; set; }

    /// <summary>Phương sai</summary>
    public decimal? VarianceValue { get; set; }

    /// <summary>Khóa phân cấp AQI (-1 nếu không phải AQI, hoặc không xác định)</summary>
    public int AqiLevelKey { get; set; } = -1;

    /// <summary>Tên file nguồn (2020 hoặc 2021)</summary>
    public string SourceFile { get; set; } = string.Empty;
}
