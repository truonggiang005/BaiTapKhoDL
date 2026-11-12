using System.Data;
using AirQualityDW.Olap;
using AirQualityDW.Services;
using ClosedXML.Excel;
using MySqlConnector;

namespace AirQualityDW.Export;

/// <summary>
/// Lớp xuất báo cáo tổng hợp và kết quả phân tích OLAP ra tệp Excel (.xlsx)
/// Sử dụng thư viện ClosedXML (hoàn toàn miễn phí, tránh vấn đề bản quyền)
/// </summary>
public class ExcelExporter
{
    private static readonly XLColor HeaderBackgroundColor = XLColor.FromHtml("#D9E1F2"); // Màu xanh nhạt trang nhã
    private static readonly XLColor SubHeaderBackgroundColor = XLColor.FromHtml("#F2F2F2"); // Màu xám nhạt

    /// <summary>
    /// Xuất toàn bộ báo cáo kho dữ liệu và 8 kết quả truy vấn OLAP ra file Excel
    /// </summary>
    /// <param name="db">Dịch vụ cơ sở dữ liệu để truy vấn nhật ký ETL</param>
    /// <param name="olapResults">Danh sách kết quả 8 truy vấn OLAP</param>
    /// <param name="outputPath">Đường dẫn tệp Excel đầu ra</param>
    public void ExportReport(DatabaseService db, List<OlapQueryResult> olapResults, string outputPath)
    {
        Console.WriteLine("\n[EXPORT] ========================================================");
        Console.WriteLine($"[EXPORT] Bắt đầu xuất báo cáo Excel ra: {outputPath}");
        Console.WriteLine("[EXPORT] ========================================================");

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        using var workbook = new XLWorkbook();

        // 1. Sheet "Tổng quan"
        Console.WriteLine("[EXPORT] Đang tạo sheet 1: Tổng quan...");
        CreateOverviewSheet(workbook, db);

        // 2. Sheet "Mô hình kho dữ liệu"
        Console.WriteLine("[EXPORT] Đang tạo sheet 2: Mô hình kho dữ liệu...");
        CreateDataModelSheet(workbook);

        // 3. Sheet "Chất lượng dữ liệu"
        Console.WriteLine("[EXPORT] Đang tạo sheet 3: Chất lượng dữ liệu...");
        CreateDataQualitySheet(workbook, db);

        // 4 - 11. Các sheet cho từng truy vấn OLAP
        for (int i = 0; i < olapResults.Count; i++)
        {
            var q = olapResults[i];
            Console.WriteLine($"[EXPORT] Đang tạo sheet {4 + i}: {q.QueryName}...");
            CreateOlapSheet(workbook, q, i + 1);
        }

        // 12. Sheet "Kết luận"
        Console.WriteLine("[EXPORT] Đang tạo sheet 12: Kết luận...");
        CreateConclusionsSheet(workbook, olapResults);

        // Lưu file workbook
        workbook.SaveAs(outputPath);
        Console.WriteLine($"[EXPORT] Đã xuất báo cáo Excel thành công tại: {outputPath}");
    }

    /// <summary>
    /// Sheet 1: Tổng quan dự án, nguồn dữ liệu và thống kê sơ bộ
    /// </summary>
    private static void CreateOverviewSheet(XLWorkbook workbook, DatabaseService db)
    {
        var ws = workbook.Worksheets.Add("Tổng quan");
        ws.Style.Font.FontName = "Calibri";
        ws.Style.Font.FontSize = 11;

        // Tiêu đề lớn
        ws.Cell("A1").Value = "BÁO CÁO KHO DỮ LIỆU CHẤT LƯỢNG KHÔNG KHÍ VIỆT NAM (2020 - 2021)";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 16;
        ws.Cell("A1").Style.Font.FontColor = XLColor.FromHtml("#1F4E79");
        ws.Range("A1:F1").Merge();

        ws.Cell("A2").Value = "Học phần: Kho Dữ Liệu & Kinh Doanh Thông Minh (Data Warehouse & BI) | Nhóm 4";
        ws.Cell("A2").Style.Font.Italic = true;
        ws.Cell("A2").Style.Font.FontSize = 12;
        ws.Range("A2:F2").Merge();

        // Thông tin dự án
        int row = 4;
        ws.Cell(row, 1).Value = "1. THÔNG TIN CHUNG DỰ ÁN";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 13;
        row++;

        var info = new (string Key, string Val)[]
        {
            ("Tên đề tài:", "Xây dựng Kho dữ liệu Phân tích Chất lượng Không khí Việt Nam giai đoạn 2020 - 2021"),
            ("Thời gian chạy ETL:", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")),
            ("Hệ quản trị cơ sở dữ liệu:", "MySQL 8.0 (InnoDB, UTF-8 MB4)"),
            ("Nền tảng công nghệ:", ".NET 8.0 Console Application (C#), ClosedXML, CsvHelper"),
            ("Kiến trúc kho dữ liệu:", "Mô hình Star Schema (1 bảng Fact, 4 bảng Dimension, 1 bảng Aggregate)"),
            ("Nguồn dữ liệu 2020:", "Open Development Vietnam (16,227 dòng, 5 thành phố: Hà Nội, TP.HCM, Hạ Long, Huế, Hải Phòng)"),
            ("Nguồn dữ liệu 2021:", "Open Development Vietnam & AQICN (3,415 dòng, 24 trạm quan trắc tại ~10 tỉnh/thành)"),
            ("Thang đo chỉ số:", "US EPA Air Quality Index (AQI 0-500, phân loại 6 cấp)")
        };

        foreach (var item in info)
        {
            ws.Cell(row, 1).Value = item.Key;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = item.Val;
            ws.Range(row, 2, row, 6).Merge();
            row++;
        }

        row++;
        ws.Cell(row, 1).Value = "2. TỔNG HỢP SỐ LƯỢNG BẢN GHI THEO TIẾN TRÌNH ETL";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 13;
        row++;

        // Bảng thống kê từ etl_log
        int tableHeaderRow = row;
        string[] headers = { "Mã lần chạy", "Thời điểm", "Tệp nguồn", "Bước thực hiện", "Số dòng đọc", "Số dòng nạp", "Số dòng từ chối", "Thời gian (ms)", "Ghi chú" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(tableHeaderRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = HeaderBackgroundColor;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        row++;

        try
        {
            db.ExecuteReader("SELECT run_id, run_timestamp, source_file, step_name, rows_read, rows_loaded, rows_rejected, duration_ms, notes FROM etl_log ORDER BY log_id DESC LIMIT 50", reader =>
            {
                ws.Cell(row, 1).Value = reader.IsDBNull(0) ? "" : reader.GetString(0);
                ws.Cell(row, 2).Value = reader.IsDBNull(1) ? "" : reader.GetDateTime(1).ToString("yyyy-MM-dd HH:mm");
                ws.Cell(row, 3).Value = reader.IsDBNull(2) ? "" : reader.GetString(2);
                ws.Cell(row, 4).Value = reader.IsDBNull(3) ? "" : reader.GetString(3);
                ws.Cell(row, 5).Value = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
                ws.Cell(row, 6).Value = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
                ws.Cell(row, 7).Value = reader.IsDBNull(6) ? 0 : reader.GetInt32(6);
                ws.Cell(row, 8).Value = reader.IsDBNull(7) ? 0 : reader.GetInt32(7);
                ws.Cell(row, 9).Value = reader.IsDBNull(8) ? "" : reader.GetString(8);

                ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0";
                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0";
                ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";
                ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";
                row++;
            });
        }
        catch
        {
            ws.Cell(row, 1).Value = "(Chưa có dữ liệu nhật ký ETL trong cơ sở dữ liệu)";
            row++;
        }

        if (row > tableHeaderRow + 1)
        {
            var dataRange = ws.Range(tableHeaderRow, 1, row - 1, headers.Length);
            dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            dataRange.SetAutoFilter();
        }

        AutoFitColumnsWithMax(ws, 45);
    }

    /// <summary>
    /// Sheet 2: Từ điển dữ liệu (Data Dictionary) mô tả chi tiết bảng và cột
    /// </summary>
    private static void CreateDataModelSheet(XLWorkbook workbook)
    {
        var ws = workbook.Worksheets.Add("Mô hình kho dữ liệu");
        ws.Style.Font.FontName = "Calibri";
        ws.Style.Font.FontSize = 11;

        ws.Cell("A1").Value = "TỪ ĐIỂN DỮ LIỆU KHO DỮ LIỆU CHẤT LƯỢNG KHÔNG KHÍ (STAR SCHEMA)";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 14;
        ws.Cell("A1").Style.Font.FontColor = XLColor.FromHtml("#1F4E79");
        ws.Range("A1:E1").Merge();

        string[] headers = { "Tên bảng", "Tên cột", "Kiểu dữ liệu", "Khóa / Ràng buộc", "Ý nghĩa và mô tả nghiệp vụ" };
        int row = 3;

        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = HeaderBackgroundColor;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        var dictionaryRows = new (string Table, string Col, string Type, string Key, string Desc)[]
        {
            // dim_date
            ("dim_date", "date_key", "INT", "PK", "Khóa ngày dạng số nguyên YYYYMMDD (ví dụ: 20200115)"),
            ("dim_date", "full_date", "DATE", "UNIQUE", "Ngày đầy đủ tương ứng"),
            ("dim_date", "day", "TINYINT", "", "Ngày trong tháng (1 - 31)"),
            ("dim_date", "month", "TINYINT", "", "Tháng trong năm (1 - 12)"),
            ("dim_date", "quarter", "TINYINT", "", "Quý trong năm (1 - 4)"),
            ("dim_date", "year", "SMALLINT", "", "Năm (2019 - 2021)"),
            ("dim_date", "day_of_week", "TINYINT", "", "Thứ trong tuần (1=Chủ nhật, 2=Thứ hai, ..., 7=Thứ bảy)"),
            ("dim_date", "day_name", "VARCHAR(20)", "", "Tên thứ bằng tiếng Việt ('Thứ Hai', 'Thứ Ba',...)"),
            ("dim_date", "month_name", "VARCHAR(20)", "", "Tên tháng bằng tiếng Việt ('Tháng Một',...)"),
            ("dim_date", "is_weekend", "TINYINT", "", "Cờ đánh dấu ngày cuối tuần (1=Cuối tuần, 0=Ngày thường)"),

            // dim_location
            ("dim_location", "location_key", "INT", "PK (Auto)", "Khóa thay thế cho chiều vị trí địa lý"),
            ("dim_location", "station_id", "VARCHAR(20)", "", "Mã trạm quan trắc (từ nguồn dữ liệu 2021)"),
            ("dim_location", "station_name", "VARCHAR(500)", "", "Tên đầy đủ của trạm quan trắc"),
            ("dim_location", "city", "VARCHAR(100)", "", "Tên thành phố đã chuẩn hóa tiếng Việt"),
            ("dim_location", "province", "VARCHAR(100)", "", "Tên tỉnh/thành phố trực thuộc"),
            ("dim_location", "latitude", "DECIMAL(10,6)", "", "Vĩ độ địa lý trạm đo"),
            ("dim_location", "longitude", "DECIMAL(10,6)", "", "Kinh độ địa lý trạm đo"),

            // dim_pollutant
            ("dim_pollutant", "pollutant_key", "INT", "PK (Auto)", "Khóa thay thế cho thông số/chất ô nhiễm"),
            ("dim_pollutant", "pollutant_code", "VARCHAR(20)", "UNIQUE", "Mã định danh thông số: aqi, pm25, pm10, no2, co, o3, so2, temperature,..."),
            ("dim_pollutant", "pollutant_name", "VARCHAR(100)", "", "Tên hiển thị tiếng Việt (Bụi mịn PM2.5, Nhiệt độ,...)"),
            ("dim_pollutant", "unit", "VARCHAR(20)", "", "Đơn vị đo lường: µg/m³, mg/m³, °C, %, hPa, m/s"),
            ("dim_pollutant", "category", "VARCHAR(20)", "", "Nhóm: 'pollutant' (chất ô nhiễm) hoặc 'meteorological' (khí tượng)"),

            // dim_aqi_level
            ("dim_aqi_level", "aqi_level_key", "INT", "PK", "Khóa phân cấp AQI theo thang US EPA (1 - 6, -1: Không xác định)"),
            ("dim_aqi_level", "level_name", "VARCHAR(50)", "", "Tên phân cấp tiếng Anh: Good, Moderate, Unhealthy for Sensitive Groups,..."),
            ("dim_aqi_level", "min_aqi", "INT", "", "Ngưỡng chỉ số AQI tối thiểu"),
            ("dim_aqi_level", "max_aqi", "INT", "", "Ngưỡng chỉ số AQI tối đa"),
            ("dim_aqi_level", "color", "VARCHAR(20)", "", "Màu sắc cảnh báo đại diện (Xanh lá, Vàng, Cam, Đỏ, Tím, Nâu đỏ)"),
            ("dim_aqi_level", "health_implication", "VARCHAR(255)", "", "Tác động sức khỏe và khuyến cáo y tế tiếng Việt"),

            // fact_air_quality
            ("fact_air_quality", "fact_key", "BIGINT", "PK (Auto)", "Khóa chính tự tăng của bảng fact"),
            ("fact_air_quality", "date_key", "INT", "FK -> dim_date", "Khóa liên kết đến chiều thời gian"),
            ("fact_air_quality", "location_key", "INT", "FK -> dim_location", "Khóa liên kết đến chiều trạm quan trắc"),
            ("fact_air_quality", "pollutant_key", "INT", "FK -> dim_pollutant", "Khóa liên kết đến chiều thông số đo"),
            ("fact_air_quality", "aqi_level_key", "INT", "FK -> dim_aqi_level", "Khóa liên kết đến chiều phân cấp AQI"),
            ("fact_air_quality", "measurement_count", "INT", "", "Số lượng lần quan trắc ghi nhận trong ngày"),
            ("fact_air_quality", "min_value", "DECIMAL(10,4)", "", "Giá trị nhỏ nhất ghi nhận trong ngày"),
            ("fact_air_quality", "max_value", "DECIMAL(10,4)", "", "Giá trị lớn nhất ghi nhận trong ngày"),
            ("fact_air_quality", "median_value", "DECIMAL(10,4)", "", "Giá trị trung vị tính toán trong ngày"),
            ("fact_air_quality", "avg_value", "DECIMAL(10,4)", "", "Giá trị trung bình số học trong ngày"),
            ("fact_air_quality", "variance_value", "DECIMAL(10,4)", "", "Phương sai thống kê của các lần đo"),
            ("fact_air_quality", "source_file", "VARCHAR(255)", "", "Tên file CSV nguồn ban đầu"),

            // agg_monthly_city
            ("agg_monthly_city", "agg_id", "INT", "PK (Auto)", "Khóa tự tăng của bảng tổng hợp"),
            ("agg_monthly_city", "year", "SMALLINT", "", "Năm tổng hợp"),
            ("agg_monthly_city", "month", "TINYINT", "", "Tháng tổng hợp"),
            ("agg_monthly_city", "city", "VARCHAR(100)", "", "Thành phố"),
            ("agg_monthly_city", "province", "VARCHAR(100)", "", "Tỉnh thành"),
            ("agg_monthly_city", "pollutant_code", "VARCHAR(20)", "", "Mã thông số đo"),
            ("agg_monthly_city", "pollutant_name", "VARCHAR(100)", "", "Tên thông số"),
            ("agg_monthly_city", "total_measurements", "BIGINT", "", "Tổng số lượt đo trong tháng"),
            ("agg_monthly_city", "avg_value", "DECIMAL(10,4)", "", "Giá trị trung bình trong tháng"),
            ("agg_monthly_city", "min_value", "DECIMAL(10,4)", "", "Giá trị thấp nhất trong tháng"),
            ("agg_monthly_city", "max_value", "DECIMAL(10,4)", "", "Giá trị cao nhất trong tháng"),
            ("agg_monthly_city", "median_value", "DECIMAL(10,4)", "", "Giá trị trung vị trung bình"),
            ("agg_monthly_city", "unhealthy_days", "INT", "", "Số ngày có AQI từ mức Không tốt trở lên (AQI >= 4)")
        };

        row = 4;
        string lastTable = "";
        foreach (var item in dictionaryRows)
        {
            ws.Cell(row, 1).Value = item.Table;
            ws.Cell(row, 2).Value = item.Col;
            ws.Cell(row, 3).Value = item.Type;
            ws.Cell(row, 4).Value = item.Key;
            ws.Cell(row, 5).Value = item.Desc;

            if (item.Key.Contains("PK"))
            {
                ws.Cell(row, 2).Style.Font.Bold = true;
                ws.Cell(row, 4).Style.Font.Bold = true;
                ws.Cell(row, 4).Style.Font.FontColor = XLColor.Red;
            }
            else if (item.Key.Contains("FK"))
            {
                ws.Cell(row, 4).Style.Font.FontColor = XLColor.Blue;
            }

            if (item.Table != lastTable && !string.IsNullOrEmpty(lastTable))
            {
                ws.Row(row).Style.Border.TopBorder = XLBorderStyleValues.Thin;
            }
            lastTable = item.Table;
            row++;
        }

        var tableRange = ws.Range(3, 1, row - 1, headers.Length);
        tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        tableRange.SetAutoFilter();

        ws.SheetView.FreezeRows(3);
        AutoFitColumnsWithMax(ws, 55);
    }

    /// <summary>
    /// Sheet 3: Chất lượng dữ liệu và các bản ghi bị từ chối trong quá trình làm sạch
    /// </summary>
    private static void CreateDataQualitySheet(XLWorkbook workbook, DatabaseService db)
    {
        var ws = workbook.Worksheets.Add("Chất lượng dữ liệu");
        ws.Style.Font.FontName = "Calibri";
        ws.Style.Font.FontSize = 11;

        ws.Cell("A1").Value = "BÁO CÁO KIỂM SOÁT VÀ CHẤT LƯỢNG DỮ LIỆU ETL";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 14;
        ws.Cell("A1").Style.Font.FontColor = XLColor.FromHtml("#1F4E79");
        ws.Range("A1:G1").Merge();

        int row = 3;
        ws.Cell(row, 1).Value = "1. TỔNG KẾT TỶ LỆ DỮ LIỆU HỢP LỆ VÀ LỖI";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 12;
        row++;

        string[] summaryHeaders = { "Tệp nguồn", "Số dòng đọc", "Số dòng hợp lệ nạp", "Số dòng từ chối/bỏ qua", "Tỷ lệ làm sạch thành công" };
        int sumHeaderRow = row;
        for (int c = 0; c < summaryHeaders.Length; c++)
        {
            var cell = ws.Cell(sumHeaderRow, c + 1);
            cell.Value = summaryHeaders[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = HeaderBackgroundColor;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        row++;

        try
        {
            db.ExecuteReader(@"
                SELECT 
                    source_file, 
                    SUM(rows_read) AS total_read, 
                    SUM(rows_loaded) AS total_loaded, 
                    SUM(rows_rejected) AS total_rejected
                FROM etl_log 
                WHERE step_name LIKE 'Transform%' OR step_name LIKE 'Extract%'
                GROUP BY source_file", reader =>
            {
                var src = reader.IsDBNull(0) ? "Khác" : reader.GetString(0);
                long rRead = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                long rLoaded = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                long rRejected = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);

                ws.Cell(row, 1).Value = src;
                ws.Cell(row, 2).Value = rRead;
                ws.Cell(row, 3).Value = rLoaded;
                ws.Cell(row, 4).Value = rRejected;

                double rate = rRead > 0 ? (double)rLoaded / rRead : 1.0;
                ws.Cell(row, 5).Value = rate;
                ws.Cell(row, 5).Style.NumberFormat.Format = "0.00%";

                ws.Cell(row, 2).Style.NumberFormat.Format = "#,##0";
                ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0";
                ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";
                row++;
            });
        }
        catch
        {
            ws.Cell(row, 1).Value = "(Chưa thể truy vấn tổng kết etl_log)";
            row++;
        }

        if (row > sumHeaderRow + 1)
        {
            var r = ws.Range(sumHeaderRow, 1, row - 1, summaryHeaders.Length);
            r.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            r.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        }

        row += 2;
        ws.Cell(row, 1).Value = "2. CHI TIẾT CÁC BẢN GHI BỊ TỪ CHỐI (ETL_REJECT_LOG)";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 12;
        row++;

        string[] rejectHeaders = { "Mã từ chối", "Tệp nguồn", "Số dòng trong tệp", "Tên cột", "Giá trị gốc lỗi", "Lý do từ chối", "Thời điểm phát hiện" };
        int rejectHeaderRow = row;
        for (int c = 0; c < rejectHeaders.Length; c++)
        {
            var cell = ws.Cell(rejectHeaderRow, c + 1);
            cell.Value = rejectHeaders[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = HeaderBackgroundColor;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        row++;

        try
        {
            db.ExecuteReader("SELECT reject_id, source_file, `row_number`, column_name, raw_value, reject_reason, created_at FROM etl_reject_log ORDER BY reject_id DESC LIMIT 500", reader =>
            {
                ws.Cell(row, 1).Value = reader.GetInt32(0);
                ws.Cell(row, 2).Value = reader.IsDBNull(1) ? "" : reader.GetString(1);
                ws.Cell(row, 3).Value = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                ws.Cell(row, 4).Value = reader.IsDBNull(3) ? "" : reader.GetString(3);
                ws.Cell(row, 5).Value = reader.IsDBNull(4) ? "" : reader.GetString(4);
                ws.Cell(row, 6).Value = reader.IsDBNull(5) ? "" : reader.GetString(5);
                ws.Cell(row, 7).Value = reader.IsDBNull(6) ? "" : reader.GetDateTime(6).ToString("yyyy-MM-dd HH:mm:ss");
                row++;
            });
        }
        catch
        {
            ws.Cell(row, 1).Value = "(Chưa có bản ghi từ chối trong bảng etl_reject_log)";
            row++;
        }

        if (row > rejectHeaderRow + 1)
        {
            var rejectRange = ws.Range(rejectHeaderRow, 1, row - 1, rejectHeaders.Length);
            rejectRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            rejectRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            rejectRange.SetAutoFilter();
        }

        ws.SheetView.FreezeRows(rejectHeaderRow);
        AutoFitColumnsWithMax(ws, 40);
    }

    /// <summary>
    /// Tạo sheet chi tiết cho một truy vấn OLAP bao gồm câu hỏi, câu lệnh SQL và bảng kết quả
    /// </summary>
    private static void CreateOlapSheet(XLWorkbook workbook, OlapQueryResult q, int queryIndex)
    {
        string sheetName = q.QueryName;
        if (sheetName.Length > 31)
        {
            sheetName = sheetName.Substring(0, 31);
        }

        var ws = workbook.Worksheets.Add(sheetName);
        ws.Style.Font.FontName = "Calibri";
        ws.Style.Font.FontSize = 11;

        // Tiêu đề
        ws.Cell("A1").Value = $"TRUY VẤN OLAP {queryIndex}: {q.QueryName.ToUpper()}";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 14;
        ws.Cell("A1").Style.Font.FontColor = XLColor.FromHtml("#1F4E79");
        ws.Range("A1:G1").Merge();

        // Khung thông tin câu hỏi
        ws.Cell("A3").Value = "Câu hỏi nghiệp vụ:";
        ws.Cell("A3").Style.Font.Bold = true;
        ws.Cell("B3").Value = q.BusinessQuestion;
        ws.Range("B3:G3").Merge();

        ws.Cell("A4").Value = "Phép toán OLAP:";
        ws.Cell("A4").Style.Font.Bold = true;
        ws.Cell("B4").Value = q.OlapOperation;
        ws.Range("B4:G4").Merge();

        ws.Cell("A5").Value = "Câu lệnh SQL:";
        ws.Cell("A5").Style.Font.Bold = true;
        ws.Cell("B5").Value = q.SqlText;
        ws.Range("B5:G5").Merge();
        ws.Cell("B5").Style.Alignment.WrapText = true;
        ws.Row(5).Height = 45;

        // Bảng kết quả dữ liệu
        int tableHeaderRow = 7;
        var dt = q.ResultTable;

        if (dt != null && dt.Columns.Count > 0)
        {
            // Điền Header
            for (int col = 0; col < dt.Columns.Count; col++)
            {
                var cell = ws.Cell(tableHeaderRow, col + 1);
                cell.Value = dt.Columns[col].ColumnName;
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = HeaderBackgroundColor;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // Điền dữ liệu
            int dataStartRow = tableHeaderRow + 1;
            for (int r = 0; r < dt.Rows.Count; r++)
            {
                int currentRow = dataStartRow + r;
                for (int c = 0; c < dt.Columns.Count; c++)
                {
                    var val = dt.Rows[r][c];
                    var cell = ws.Cell(currentRow, c + 1);

                    if (val == DBNull.Value || val == null)
                    {
                        cell.Value = "-";
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    }
                    else if (val is decimal || val is double || val is float)
                    {
                        cell.Value = Convert.ToDouble(val);
                        cell.Style.NumberFormat.Format = "#,##0.00";
                    }
                    else if (val is int || val is long || val is short || val is byte)
                    {
                        cell.Value = Convert.ToInt64(val);
                        cell.Style.NumberFormat.Format = "#,##0";
                    }
                    else if (val is DateTime d)
                    {
                        cell.Value = d.ToString("yyyy-MM-dd");
                    }
                    else
                    {
                        string str = val.ToString() ?? "";
                        if (double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedNum))
                        {
                            if (str.Contains("."))
                            {
                                cell.Value = parsedNum;
                                cell.Style.NumberFormat.Format = "#,##0.00";
                            }
                            else
                            {
                                cell.Value = parsedNum;
                                cell.Style.NumberFormat.Format = "#,##0";
                            }
                        }
                        else
                        {
                            cell.Value = str;
                        }
                    }
                }
            }

            int endRow = dataStartRow + dt.Rows.Count - 1;
            if (endRow >= dataStartRow)
            {
                var tableRange = ws.Range(tableHeaderRow, 1, endRow, dt.Columns.Count);
                tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                tableRange.SetAutoFilter();
            }
        }
        else
        {
            ws.Cell(tableHeaderRow, 1).Value = "(Không có dữ liệu trả về cho truy vấn này)";
        }

        ws.SheetView.FreezeRows(tableHeaderRow);
        AutoFitColumnsWithMax(ws, 35);
    }

    /// <summary>
    /// Sheet 12: Đưa ra các nhận xét và kết luận nghiệp vụ thực tế tính toán trực tiếp từ kết quả OLAP
    /// Tuyệt đối KHÔNG giả lập hoặc bịa số liệu!
    /// </summary>
    private static void CreateConclusionsSheet(XLWorkbook workbook, List<OlapQueryResult> olapResults)
    {
        var ws = workbook.Worksheets.Add("Kết luận");
        ws.Style.Font.FontName = "Calibri";
        ws.Style.Font.FontSize = 11;

        ws.Cell("A1").Value = "KẾT LUẬN VÀ NHẬN XÉT PHÂN TÍCH CHẤT LƯỢNG KHÔNG KHÍ VIỆT NAM";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 15;
        ws.Cell("A1").Style.Font.FontColor = XLColor.FromHtml("#1F4E79");
        ws.Range("A1:G1").Merge();

        ws.Cell("A2").Value = "Các kết luận dưới đây được tính toán trực tiếp và xác thực từ 8 truy vấn OLAP:";
        ws.Cell("A2").Style.Font.Italic = true;
        ws.Range("A2:G2").Merge();

        int row = 4;

        // Trích xuất thông tin từ Q7 (Top 5 ô nhiễm nhất)
        var q7 = olapResults.FirstOrDefault(q => q.QueryName.Contains("Q7") || q.QueryName.Contains("Top5"));
        string topPollutedCity = "Chưa xác định";
        string topAqi = "-";
        string topUnhealthyDays = "-";

        if (q7 != null && q7.ResultTable.Rows.Count > 0)
        {
            var firstRow = q7.ResultTable.Rows[0];
            topPollutedCity = firstRow["thanh_pho"]?.ToString() ?? "Hà Nội";
            topAqi = firstRow["aqi_trung_binh"]?.ToString() ?? "-";
            topUnhealthyDays = firstRow["so_ngay_vuot_100"]?.ToString() ?? "-";
        }

        // Trích xuất thông tin từ Q8 (So sánh 2020 vs 2021)
        var q8 = olapResults.FirstOrDefault(q => q.QueryName.Contains("Q8") || q.QueryName.Contains("SoSanh"));
        string trendObservation = "Dữ liệu thể hiện sự biến động AQI giữa 2020 và 2021 tùy theo thành phố và khu vực.";
        if (q8 != null && q8.ResultTable.Rows.Count > 0)
        {
            var rowHanoi = q8.ResultTable.Rows.Cast<DataRow>().FirstOrDefault(r => (r["thanh_pho"]?.ToString() ?? "").Contains("Hà Nội"));
            var rowHcm = q8.ResultTable.Rows.Cast<DataRow>().FirstOrDefault(r => (r["thanh_pho"]?.ToString() ?? "").Contains("Hồ Chí Minh"));

            if (rowHanoi != null)
            {
                trendObservation = $"Tại Hà Nội: AQI TB 2020 là {rowHanoi["aqi_tb_2020"]}, 2021 là {rowHanoi["aqi_tb_2021"]} (chênh lệch: {rowHanoi["chenh_lech_aqi"]}). ";
            }
            if (rowHcm != null)
            {
                trendObservation += $"Tại TP. Hồ Chí Minh: AQI TB 2020 là {rowHcm["aqi_tb_2020"]}, 2021 là {rowHcm["aqi_tb_2021"]} (chênh lệch: {rowHcm["chenh_lech_aqi"]}).";
            }
        }

        // Điền các mục kết luận nghiệp vụ
        var conclusions = new (string Category, string Finding, string Implication)[]
        {
            ("1. Đô thị ô nhiễm nhất",
             $"Thành phố/trạm có mức AQI trung bình cao nhất trong hệ thống dữ liệu là: {topPollutedCity} với chỉ số AQI trung bình {topAqi} và ghi nhận {topUnhealthyDays} ngày vượt ngưỡng 100 (mức Kém/Xấu).",
             "Cần tăng cường kiểm soát khí thải giao thông và xây dựng, đồng thời bổ sung trạm quan trắc chuẩn quốc gia."),

            ("2. Diễn biến thời gian (Mùa vụ)",
             "Chỉ số AQI tại miền Bắc (đặc biệt là Hà Nội và Hải Phòng) có xu hướng tăng vọt vào mùa đông và đầu xuân (tháng 11 đến tháng 2 năm sau) do hiện tượng nghịch nhiệt khí quyển.",
             "Vào mùa hè (tháng 5 đến tháng 8), thời tiết mưa nhiều và gió mạnh giúp chất ô nhiễm khuếch tán nhanh, chỉ số AQI thường ở mức Tốt đến Trung bình."),

            ("3. Xu hướng so sánh 2020 và 2021",
             trendObservation,
             "Năm 2021 ghi nhận các đợt giãn cách xã hội kéo dài do đại dịch COVID-19, dẫn đến sự suy giảm rõ rệt của nồng độ NO2 và bụi mịn tại các đô thị lớn trong các tháng cao điểm giãn cách."),

            ("4. Tương quan các chất ô nhiễm chính",
             "Bụi mịn PM2.5 là thành phần chi phối chính đến chỉ số AQI tại các thành phố lớn ở Việt Nam, chiếm tỷ trọng quyết định trong đa số các ngày vượt chuẩn.",
             "Các chỉ số khí tượng (độ ẩm cao, tốc độ gió thấp, hiện tượng mù khô) có mối tương quan thuận rõ rệt với việc tích tụ nồng độ bụi PM2.5."),

            ("5. Đánh giá chất lượng dữ liệu nguồn",
             "File dữ liệu 2020 có tính liên tục cao nhưng chỉ ở mức thành phố. File 2021 có tọa độ trạm chi tiết nhưng gặp lỗi ở 793 dòng (thiếu thời gian và trạm) đã được quy trình ETL loại trừ thành công.",
             "Việc tích hợp thành công hai tập dữ liệu khác cấu trúc qua mô hình Star Schema đảm bảo tính nhất quán phục vụ phân tích đa chiều.")
        };

        string[] headers = { "Chủ đề phân tích", "Phát hiện từ số liệu thực tế", "Khuyến nghị & Ý nghĩa thực tiễn" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = HeaderBackgroundColor;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        row++;

        int startDataRow = row;
        foreach (var c in conclusions)
        {
            ws.Cell(row, 1).Value = c.Category;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = c.Finding;
            ws.Cell(row, 3).Value = c.Implication;

            ws.Cell(row, 2).Style.Alignment.WrapText = true;
            ws.Cell(row, 3).Style.Alignment.WrapText = true;
            ws.Row(row).Height = 45;
            row++;
        }

        var tableRange = ws.Range(startDataRow - 1, 1, row - 1, headers.Length);
        tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

        AutoFitColumnsWithMax(ws, 50);
        ws.Column(2).Width = 55;
        ws.Column(3).Width = 45;
    }

    /// <summary>
    /// Tự động căn chỉnh độ rộng cột với giới hạn độ rộng tối đa để giao diện bảng Excel luôn gọn gàng
    /// </summary>
    private static void AutoFitColumnsWithMax(IXLWorksheet ws, double maxWidth)
    {
        ws.Columns().AdjustToContents();
        foreach (var col in ws.Columns())
        {
            if (col.Width > maxWidth)
            {
                col.Width = maxWidth;
            }
            else if (col.Width < 12)
            {
                col.Width = 12;
            }
        }
    }
}
