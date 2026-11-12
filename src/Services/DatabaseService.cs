using System.Data;
using System.Text;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace AirQualityDW.Services;

/// <summary>
/// Dịch vụ kết nối và thao tác với cơ sở dữ liệu MySQL
/// Cung cấp các phương thức tiện ích để thực thi SQL và quản lý kết nối
/// </summary>
public class DatabaseService
{
    private readonly string _connectionString;

    /// <summary>
    /// Khởi tạo DatabaseService từ cấu hình ứng dụng
    /// </summary>
    /// <param name="config">Đối tượng cấu hình IConfiguration</param>
    public DatabaseService(IConfiguration config)
    {
        // Đọc chuỗi kết nối từ appsettings.json
        _connectionString = config.GetConnectionString("MySQL")
            ?? throw new InvalidOperationException("Không tìm thấy connection string 'MySQL' trong cấu hình.");
    }

    /// <summary>
    /// Khởi tạo DatabaseService từ chuỗi kết nối trực tiếp (dùng với AppConfig)
    /// </summary>
    /// <param name="connectionString">Chuỗi kết nối MySQL</param>
    public DatabaseService(string connectionString)
    {
        _connectionString = connectionString
            ?? throw new ArgumentNullException(nameof(connectionString), "Chuỗi kết nối MySQL không được để trống.");
    }

    /// <summary>
    /// Đảm bảo cơ sở dữ liệu MySQL đã tồn tại. Nếu chưa, tạo mới database với utf8mb4.
    /// </summary>
    public void EnsureDatabaseCreated()
    {
        try
        {
            var builder = new MySqlConnectionStringBuilder(_connectionString);
            string targetDb = builder.Database;

            if (string.IsNullOrEmpty(targetDb))
                return;

            // Kết nối mà không chỉ định Database để tạo mới nếu cần
            builder.Database = "";
            using var serverConn = new MySqlConnection(builder.ConnectionString);
            serverConn.Open();

            using var cmd = new MySqlCommand(
                $"CREATE DATABASE IF NOT EXISTS `{targetDb}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;",
                serverConn);
            cmd.ExecuteNonQuery();
            Console.WriteLine($"[INFO] Đã xác nhận cơ sở dữ liệu `{targetDb}` sẵn sàng.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Không thể kiểm tra/tạo database tự động: {ex.Message}");
        }
    }

    /// <summary>
    /// Tạo kết nối mới đến cơ sở dữ liệu MySQL
    /// </summary>
    public MySqlConnection GetConnection()
    {
        return new MySqlConnection(_connectionString);
    }

    /// <summary>
    /// Thực thi lệnh SQL không trả về dữ liệu (INSERT, UPDATE, DELETE, DDL)
    /// </summary>
    /// <param name="sql">Câu lệnh SQL</param>
    /// <param name="parameters">Tham số truyền vào (tùy chọn)</param>
    /// <returns>Số dòng bị ảnh hưởng</returns>
    public int ExecuteNonQuery(string sql, Dictionary<string, object?>? parameters = null)
    {
        try
        {
            using var conn = GetConnection();
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);

            // Thêm tham số vào câu lệnh để tránh SQL injection
            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                }
            }

            return cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LỖI] ExecuteNonQuery thất bại: {ex.Message}");
            Console.WriteLine($"       SQL: {sql[..Math.Min(200, sql.Length)]}");
            throw;
        }
    }

    /// <summary>
    /// Thực thi lệnh SQL và trả về giá trị đơn
    /// </summary>
    /// <typeparam name="T">Kiểu dữ liệu trả về</typeparam>
    /// <param name="sql">Câu lệnh SQL</param>
    /// <param name="parameters">Tham số truyền vào (tùy chọn)</param>
    /// <returns>Giá trị đơn kiểu T</returns>
    public T? ExecuteScalar<T>(string sql, Dictionary<string, object?>? parameters = null)
    {
        try
        {
            using var conn = GetConnection();
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);

            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                }
            }

            var result = cmd.ExecuteScalar();
            if (result == null || result == DBNull.Value)
                return default;

            return (T)Convert.ChangeType(result, typeof(T));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LỖI] ExecuteScalar thất bại: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Thực thi lệnh SQL và xử lý kết quả qua callback
    /// </summary>
    /// <param name="sql">Câu lệnh SQL SELECT</param>
    /// <param name="action">Hàm xử lý từng dòng kết quả</param>
    /// <param name="parameters">Tham số truyền vào (tùy chọn)</param>
    public void ExecuteReader(string sql, Action<MySqlDataReader> action, Dictionary<string, object?>? parameters = null)
    {
        try
        {
            using var conn = GetConnection();
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);

            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                }
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                action(reader);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LỖI] ExecuteReader thất bại: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Thực thi file SQL, xử lý DELIMITER $$ cho stored procedures
    /// Tách câu lệnh theo dấu ';' hoặc theo DELIMITER tùy chỉnh
    /// </summary>
    /// <param name="filePath">Đường dẫn đến file .sql</param>
    public void ExecuteSqlFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Không tìm thấy file SQL: {filePath}");

        Console.WriteLine($"[INFO] Đang thực thi file SQL: {Path.GetFileName(filePath)}");

        // Đọc toàn bộ nội dung file
        var content = File.ReadAllText(filePath, Encoding.UTF8);

        // Tách các câu lệnh, xử lý DELIMITER $$ cho stored procedures
        var statements = ParseSqlStatements(content);

        try
        {
            using var conn = GetConnection();
            conn.Open();

            int successCount = 0;
            int skipCount = 0;

            foreach (var statement in statements)
            {
                var trimmed = statement.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    skipCount++;
                    continue;
                }

                try
                {
                    using var cmd = new MySqlCommand(trimmed, conn);
                    // Tăng timeout cho các câu lệnh phức tạp (ví dụ: stored procedure populate ngày)
                    cmd.CommandTimeout = 300;
                    cmd.ExecuteNonQuery();
                    successCount++;
                }
                catch (MySqlException ex) when (ex.Message.Contains("already exists"))
                {
                    // Bỏ qua lỗi khi bảng/procedure đã tồn tại
                    Console.WriteLine($"[WARN] Bỏ qua (đã tồn tại): {trimmed[..Math.Min(80, trimmed.Length)]}...");
                    skipCount++;
                }
            }

            Console.WriteLine($"[INFO] Hoàn thành: {successCount} câu lệnh thành công, {skipCount} bỏ qua.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LỖI] ExecuteSqlFile thất bại: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Phân tích file SQL thành danh sách các câu lệnh riêng lẻ
    /// Xử lý đúng DELIMITER $$ và chuỗi được trích dẫn
    /// </summary>
    private static List<string> ParseSqlStatements(string content)
    {
        var statements = new List<string>();
        var currentDelimiter = ";";
        var lines = content.Split('\n');
        var currentStatement = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var trimmedLine = line.Trim();

            // Kiểm tra lệnh DELIMITER
            if (trimmedLine.StartsWith("DELIMITER", StringComparison.OrdinalIgnoreCase))
            {
                // Lưu câu lệnh hiện tại nếu có
                var pending = currentStatement.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(pending))
                {
                    statements.Add(pending);
                    currentStatement.Clear();
                }

                // Cập nhật delimiter mới
                var parts = trimmedLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    currentDelimiter = parts[1].Trim();
                }
                continue;
            }

            currentStatement.AppendLine(line);

            // Kiểm tra xem dòng hiện tại có kết thúc bằng delimiter không
            var currentText = currentStatement.ToString().TrimEnd();
            if (currentText.EndsWith(currentDelimiter))
            {
                // Loại bỏ delimiter ở cuối
                var stmt = currentText[..^currentDelimiter.Length].Trim();
                if (!string.IsNullOrWhiteSpace(stmt))
                {
                    statements.Add(stmt);
                }
                currentStatement.Clear();
            }
        }

        // Thêm phần còn lại nếu có
        var remaining = currentStatement.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(remaining))
        {
            statements.Add(remaining);
        }

        return statements;
    }

    /// <summary>
    /// Thực thi bulk insert với transaction và batching để tối ưu hiệu suất
    /// </summary>
    /// <typeparam name="T">Kiểu dữ liệu của từng bản ghi</typeparam>
    /// <param name="items">Danh sách bản ghi cần insert</param>
    /// <param name="batchSize">Số bản ghi mỗi batch (mặc định 500)</param>
    /// <param name="insertAction">Hàm thực hiện insert một batch</param>
    public void BulkInsert<T>(
        IList<T> items,
        int batchSize,
        Action<MySqlConnection, MySqlTransaction, IList<T>> insertAction)
    {
        if (items.Count == 0)
        {
            Console.WriteLine("[INFO] Không có dữ liệu để bulk insert.");
            return;
        }

        try
        {
            using var conn = GetConnection();
            conn.Open();

            int totalInserted = 0;
            int batchCount = 0;

            // Chia thành các batch và thực thi trong transaction
            for (int i = 0; i < items.Count; i += batchSize)
            {
                var batch = items.Skip(i).Take(batchSize).ToList();

                using var transaction = conn.BeginTransaction();
                try
                {
                    insertAction(conn, transaction, batch);
                    transaction.Commit();
                    totalInserted += batch.Count;
                    batchCount++;

                    if (batchCount % 10 == 0)
                    {
                        Console.WriteLine($"[INFO] Đã nạp {totalInserted}/{items.Count} bản ghi...");
                    }
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine($"[LỖI] Batch {batchCount} thất bại, đã rollback: {ex.Message}");
                    throw;
                }
            }

            Console.WriteLine($"[INFO] Bulk insert hoàn thành: {totalInserted} bản ghi trong {batchCount} batch.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LỖI] BulkInsert thất bại: {ex.Message}");
            throw;
        }
    }
}
