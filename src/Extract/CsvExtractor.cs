using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using AirQualityDW.Models;
using AirQualityDW.Services;
using MySqlConnector;

namespace AirQualityDW.Extract;

/// <summary>
/// Lớp trích xuất dữ liệu từ file CSV vào bảng staging trong cơ sở dữ liệu
/// Thực hiện bước E (Extract) trong quy trình ETL
/// </summary>
public class CsvExtractor
{
    /// <summary>
    /// Trích xuất dữ liệu từ file CSV năm 2020 vào bảng staging_2020
    /// Thực hiện idempotent: TRUNCATE trước khi nạp dữ liệu mới
    /// </summary>
    /// <param name="db">Dịch vụ cơ sở dữ liệu</param>
    /// <param name="csvPath">Đường dẫn đến file CSV 2020</param>
    /// <param name="runId">Mã định danh lần chạy ETL</param>
    /// <returns>Tuple (số dòng đọc được, số dòng nạp thành công)</returns>
    public (int rowsRead, int rowsLoaded) ExtractFile2020(DatabaseService db, string csvPath, string runId)
    {
        Console.WriteLine($"[EXTRACT] Bắt đầu trích xuất file 2020: {csvPath}");
        var startTime = DateTime.Now;
        int rowsRead = 0;
        int rowsLoaded = 0;

        try
        {
            // Xóa dữ liệu cũ để đảm bảo idempotent
            Console.WriteLine("[EXTRACT] Đang xóa dữ liệu cũ trong staging_2020...");
            db.ExecuteNonQuery("TRUNCATE TABLE staging_2020");

            // Đọc file CSV với CsvHelper
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = ",",
                HasHeaderRecord = true,
                MissingFieldFound = null,         // Bỏ qua trường thiếu
                BadDataFound = null,               // Bỏ qua dữ liệu lỗi
                TrimOptions = TrimOptions.Trim,
            };

            var records = new List<Staging2020Record>();

            using (var reader = new StreamReader(csvPath, System.Text.Encoding.UTF8))
            using (var csv = new CsvReader(reader, config))
            {
                csv.Read();
                csv.ReadHeader();

                while (csv.Read())
                {
                    try
                    {
                        var record = csv.GetRecord<Staging2020Record>();
                        if (record != null)
                        {
                            records.Add(record);
                            rowsRead++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Bỏ qua dòng lỗi #{rowsRead + 1}: {ex.Message}");
                    }
                }
            }

            Console.WriteLine($"[EXTRACT] Đã đọc {rowsRead} dòng từ file 2020. Đang nạp vào staging...");

            // Nạp dữ liệu vào staging_2020 theo batch
            const int batchSize = 500;
            var sourceFileName = Path.GetFileName(csvPath);

            db.BulkInsert(records, batchSize, (conn, transaction, batch) =>
            {
                const string insertSql = @"
                    INSERT INTO staging_2020 
                        (raw_date, country, city, specie, measurement_count, min_value, max_value, median_value, variance_value, source_file)
                    VALUES 
                        (@raw_date, @country, @city, @specie, @measurement_count, @min_value, @max_value, @median_value, @variance_value, @source_file)";

                foreach (var rec in batch)
                {
                    using var cmd = new MySqlCommand(insertSql, conn, transaction);
                    cmd.Parameters.AddWithValue("@raw_date", (object?)rec.Date ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@country", (object?)rec.Country ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@city", (object?)rec.City ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@specie", (object?)rec.Specie ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@measurement_count", (object?)rec.Count ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@min_value", (object?)rec.Min ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@max_value", (object?)rec.Max ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@median_value", (object?)rec.Median ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@variance_value", (object?)rec.Variance ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@source_file", sourceFileName);
                    cmd.ExecuteNonQuery();
                    rowsLoaded++;
                }
            });

            var durationMs = (int)(DateTime.Now - startTime).TotalMilliseconds;

            // Ghi log vào etl_log
            LogEtl(db, runId, sourceFileName, "Extract_2020", rowsRead, rowsLoaded, 0, durationMs,
                $"Trích xuất thành công từ {csvPath}");

            Console.WriteLine($"[EXTRACT] Hoàn thành file 2020: {rowsLoaded}/{rowsRead} dòng trong {durationMs}ms");
            return (rowsRead, rowsLoaded);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LỖI] ExtractFile2020 thất bại: {ex.Message}");
            var durationMs = (int)(DateTime.Now - startTime).TotalMilliseconds;
            LogEtl(db, runId, Path.GetFileName(csvPath), "Extract_2020", rowsRead, rowsLoaded, 0, durationMs,
                $"LỖI: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Trích xuất dữ liệu từ file CSV năm 2021 vào bảng staging_2021
    /// Xử lý đặc biệt trường Location có dấu phẩy trong dấu ngoặc kép
    /// </summary>
    /// <param name="db">Dịch vụ cơ sở dữ liệu</param>
    /// <param name="csvPath">Đường dẫn đến file CSV 2021</param>
    /// <param name="runId">Mã định danh lần chạy ETL</param>
    /// <returns>Tuple (số dòng đọc được, số dòng nạp thành công)</returns>
    public (int rowsRead, int rowsLoaded) ExtractFile2021(DatabaseService db, string csvPath, string runId)
    {
        Console.WriteLine($"[EXTRACT] Bắt đầu trích xuất file 2021: {csvPath}");
        var startTime = DateTime.Now;
        int rowsRead = 0;
        int rowsLoaded = 0;

        try
        {
            // Xóa dữ liệu cũ để đảm bảo idempotent
            Console.WriteLine("[EXTRACT] Đang xóa dữ liệu cũ trong staging_2021...");
            db.ExecuteNonQuery("TRUNCATE TABLE staging_2021");

            // Cấu hình CsvHelper, bật chế độ trích dẫn để xử lý "10.78,106.70"
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = ",",
                HasHeaderRecord = true,
                MissingFieldFound = null,
                BadDataFound = null,
                TrimOptions = TrimOptions.Trim,
            };

            var records = new List<Staging2021Record>();

            using (var reader = new StreamReader(csvPath, System.Text.Encoding.UTF8))
            using (var csv = new CsvReader(reader, config))
            {
                csv.Read();
                csv.ReadHeader();

                while (csv.Read())
                {
                    try
                    {
                        var record = csv.GetRecord<Staging2021Record>();
                        if (record != null)
                        {
                            records.Add(record);
                            rowsRead++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Bỏ qua dòng lỗi #{rowsRead + 1} trong file 2021: {ex.Message}");
                    }
                }
            }

            Console.WriteLine($"[EXTRACT] Đã đọc {rowsRead} dòng từ file 2021. Đang nạp vào staging...");

            // Nạp dữ liệu vào staging_2021 theo batch
            const int batchSize = 500;
            var sourceFileName = Path.GetFileName(csvPath);

            db.BulkInsert(records, batchSize, (conn, transaction, batch) =>
            {
                const string insertSql = @"
                    INSERT INTO staging_2021 
                        (station_id, aqi_index, location, station_name, url, dominant_pollutant,
                         co, dew, humidity, no2, o3, pressure, pm10, pm25, so2, temperature, wind,
                         data_time, data_time_tz, status, alert_level, source_file)
                    VALUES 
                        (@station_id, @aqi_index, @location, @station_name, @url, @dominant_pollutant,
                         @co, @dew, @humidity, @no2, @o3, @pressure, @pm10, @pm25, @so2, @temperature, @wind,
                         @data_time, @data_time_tz, @status, @alert_level, @source_file)";

                foreach (var rec in batch)
                {
                    using var cmd = new MySqlCommand(insertSql, conn, transaction);
                    cmd.Parameters.AddWithValue("@station_id", (object?)rec.StationId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@aqi_index", (object?)rec.AqiIndex ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@location", (object?)rec.Location ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@station_name", (object?)rec.StationName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@url", (object?)rec.Url ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@dominant_pollutant", (object?)rec.DominentPollutant ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@co", (object?)rec.Co ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@dew", (object?)rec.Dew ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@humidity", (object?)rec.Humidity ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@no2", (object?)rec.No2 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@o3", (object?)rec.O3 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@pressure", (object?)rec.Pressure ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@pm10", (object?)rec.Pm10 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@pm25", (object?)rec.Pm25 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@so2", (object?)rec.So2 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@temperature", (object?)rec.Temperature ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@wind", (object?)rec.Wind ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@data_time", (object?)rec.DataTimeS ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@data_time_tz", (object?)rec.DataTimeTz ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@status", (object?)rec.Status ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@alert_level", (object?)rec.AlertLevel ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@source_file", sourceFileName);
                    cmd.ExecuteNonQuery();
                    rowsLoaded++;
                }
            });

            var durationMs = (int)(DateTime.Now - startTime).TotalMilliseconds;

            // Ghi log vào etl_log
            LogEtl(db, runId, sourceFileName, "Extract_2021", rowsRead, rowsLoaded, 0, durationMs,
                $"Trích xuất thành công từ {csvPath}");

            Console.WriteLine($"[EXTRACT] Hoàn thành file 2021: {rowsLoaded}/{rowsRead} dòng trong {durationMs}ms");
            return (rowsRead, rowsLoaded);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LỖI] ExtractFile2021 thất bại: {ex.Message}");
            var durationMs = (int)(DateTime.Now - startTime).TotalMilliseconds;
            LogEtl(db, runId, Path.GetFileName(csvPath), "Extract_2021", rowsRead, rowsLoaded, 0, durationMs,
                $"LỖI: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Ghi nhật ký ETL vào bảng etl_log
    /// </summary>
    private static void LogEtl(DatabaseService db, string runId, string sourceFile, string stepName,
        int rowsRead, int rowsLoaded, int rowsRejected, int durationMs, string notes)
    {
        try
        {
            const string sql = @"
                INSERT INTO etl_log (run_id, source_file, step_name, rows_read, rows_loaded, rows_rejected, duration_ms, notes)
                VALUES (@run_id, @source_file, @step_name, @rows_read, @rows_loaded, @rows_rejected, @duration_ms, @notes)";

            db.ExecuteNonQuery(sql, new Dictionary<string, object?>
            {
                ["@run_id"] = runId,
                ["@source_file"] = sourceFile,
                ["@step_name"] = stepName,
                ["@rows_read"] = rowsRead,
                ["@rows_loaded"] = rowsLoaded,
                ["@rows_rejected"] = rowsRejected,
                ["@duration_ms"] = durationMs,
                ["@notes"] = notes
            });
        }
        catch (Exception ex)
        {
            // Lỗi ghi log không nên làm hỏng quy trình chính
            Console.WriteLine($"[WARN] Không thể ghi vào etl_log: {ex.Message}");
        }
    }
}
