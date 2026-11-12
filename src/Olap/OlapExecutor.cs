using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using AirQualityDW.Services;
using MySqlConnector;

namespace AirQualityDW.Olap;

/// <summary>
/// Kết quả của một truy vấn OLAP
/// </summary>
public class OlapQueryResult
{
    /// <summary>Tên ngắn gọn của truy vấn (dùng làm tên sheet trong Excel, tối đa 31 ký tự)</summary>
    public string QueryName { get; set; } = string.Empty;

    /// <summary>Câu hỏi nghiệp vụ / mục tiêu phân tích</summary>
    public string BusinessQuestion { get; set; } = string.Empty;

    /// <summary>Phép toán OLAP (Roll-up, Drill-down, Slice, Dice, Pivot, Ranking,...)</summary>
    public string OlapOperation { get; set; } = string.Empty;

    /// <summary>Đoạn mã SQL thực thi</summary>
    public string SqlText { get; set; } = string.Empty;

    /// <summary>Bảng dữ liệu kết quả trả về</summary>
    public DataTable ResultTable { get; set; } = new();
}

/// <summary>
/// Lớp thực thi và quản lý 8 truy vấn phân tích OLAP
/// </summary>
public class OlapExecutor
{
    /// <summary>
    /// Đọc file sql/03_olap_queries.sql, phân tích thành các truy vấn riêng biệt và thực thi
    /// </summary>
    /// <param name="db">Dịch vụ cơ sở dữ liệu MySQL</param>
    /// <param name="sqlFilePath">Đường dẫn file sql/03_olap_queries.sql</param>
    /// <returns>Danh sách kết quả 8 truy vấn OLAP</returns>
    public List<OlapQueryResult> ExecuteAllQueries(DatabaseService db, string sqlFilePath)
    {
        Console.WriteLine("\n[OLAP] ========================================================");
        Console.WriteLine("[OLAP] Bắt đầu thực thi 8 truy vấn phân tích OLAP...");
        Console.WriteLine("[OLAP] ========================================================");

        if (!File.Exists(sqlFilePath))
        {
            throw new FileNotFoundException($"Không tìm thấy file SQL truy vấn OLAP tại: {sqlFilePath}");
        }

        var results = new List<OlapQueryResult>();
        var queries = ParseOlapQueries(sqlFilePath);

        using var conn = db.GetConnection();
        conn.Open();

        for (int i = 0; i < queries.Count; i++)
        {
            var q = queries[i];
            Console.WriteLine($"\n--- [{i + 1}/8] {q.QueryName} ---");
            Console.WriteLine($"Câu hỏi nghiệp vụ: {q.BusinessQuestion}");
            Console.WriteLine($"Phép toán OLAP: {q.OlapOperation}");

            try
            {
                using var cmd = new MySqlCommand(q.SqlText, conn);
                cmd.CommandTimeout = 180;

                using var adapter = new MySqlDataAdapter(cmd);
                var dt = new DataTable(q.QueryName);
                adapter.Fill(dt);
                q.ResultTable = dt;

                Console.WriteLine($"-> Trả về: {dt.Rows.Count} dòng, {dt.Columns.Count} cột.");

                // In xem trước tối đa 5 dòng đầu ra màn hình console
                PrintDataTablePreview(dt);

                results.Add(q);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LỖI] Thực thi truy vấn '{q.QueryName}' thất bại: {ex.Message}");
                // Gán DataTable rỗng để không ngắt quy trình
                q.ResultTable = new DataTable(q.QueryName);
                results.Add(q);
            }
        }

        Console.WriteLine("\n[OLAP] Hoàn thành thực thi toàn bộ 8 truy vấn OLAP.");
        return results;
    }

    /// <summary>
    /// Phân tích file sql/03_olap_queries.sql để trích xuất tên, câu hỏi, phép toán và câu lệnh SQL
    /// </summary>
    public List<OlapQueryResult> ParseOlapQueries(string sqlFilePath)
    {
        var rawContent = File.ReadAllText(sqlFilePath, Encoding.UTF8);
        var queries = new List<OlapQueryResult>();

        // Tách các khối truy vấn bắt đầu bằng TRUY VẤN X
        var blockPattern = new Regex(@"--\s*TRUY VẤN\s*(\d+)[:\s\-]+([^\r\n]+)(.*?)(?=(?:--\s*TRUY VẤN\s*\d+)|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var matches = blockPattern.Matches(rawContent);

        if (matches.Count >= 8)
        {
            foreach (Match m in matches)
            {
                var queryNum = m.Groups[1].Value.Trim();
                var queryTitle = m.Groups[2].Value.Trim();
                var body = m.Groups[3].Value.Trim();

                string question = "";
                string operation = "";
                var sqlBuilder = new StringBuilder();

                var lines = body.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("-- Câu hỏi:", StringComparison.OrdinalIgnoreCase))
                    {
                        question = trimmed.Substring("-- Câu hỏi:".Length).Trim();
                    }
                    else if (trimmed.StartsWith("--") && string.IsNullOrEmpty(operation) && trimmed.Contains("Phép toán OLAP:", StringComparison.OrdinalIgnoreCase))
                    {
                        operation = trimmed.Substring(trimmed.IndexOf("Phép toán OLAP:", StringComparison.OrdinalIgnoreCase) + "Phép toán OLAP:".Length).Trim();
                    }
                    else if (trimmed.StartsWith("--"))
                    {
                        // Kiểm tra nếu là câu hỏi nối tiếp nhiều dòng
                        if (!string.IsNullOrEmpty(question) && string.IsNullOrEmpty(operation) && !trimmed.StartsWith("-- ==") && !trimmed.StartsWith("-- Mức"))
                        {
                            var extra = trimmed.TrimStart('-', ' ').Trim();
                            if (!string.IsNullOrEmpty(extra) && !extra.StartsWith("Phép toán", StringComparison.OrdinalIgnoreCase))
                            {
                                question += " " + extra;
                            }
                        }
                    }
                    else
                    {
                        sqlBuilder.AppendLine(line);
                    }
                }

                var sqlText = sqlBuilder.ToString().Trim();
                // Loại bỏ lệnh USE nếu có sót
                if (sqlText.StartsWith("USE ", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = sqlText.IndexOf(';');
                    if (idx >= 0 && idx < sqlText.Length - 1)
                    {
                        sqlText = sqlText[(idx + 1)..].Trim();
                    }
                }

                string sheetName = $"Q{queryNum}_{queryTitle}".Replace(":", "").Replace("–", "-");
                if (sheetName.Length > 31)
                {
                    sheetName = sheetName.Substring(0, 31);
                }

                queries.Add(new OlapQueryResult
                {
                    QueryName = sheetName,
                    BusinessQuestion = string.IsNullOrWhiteSpace(question) ? queryTitle : question,
                    OlapOperation = string.IsNullOrWhiteSpace(operation) ? "OLAP Analysis" : operation,
                    SqlText = sqlText
                });
            }
        }

        // Nếu regex không lấy đủ 8 truy vấn (do định dạng file có thể sai khác), dùng danh sách chuẩn hóa cố định
        if (queries.Count < 8)
        {
            queries = GetDefaultQueries();
        }

        return queries;
    }

    /// <summary>
    /// Danh sách 8 truy vấn OLAP chuẩn xác theo đúng yêu cầu đề tài
    /// </summary>
    private static List<OlapQueryResult> GetDefaultQueries()
    {
        return new List<OlapQueryResult>
        {
            new OlapQueryResult
            {
                QueryName = "Q1_Rollup_ThoiGian",
                BusinessQuestion = "Chỉ số AQI trung bình thay đổi như thế nào khi cuộn lên từ Tháng → Quý → Năm?",
                OlapOperation = "ROLL-UP (GROUP BY ... WITH ROLLUP)",
                SqlText = @"SELECT
  d.year                           AS nam,
  d.quarter                        AS quy,
  d.month                          AS thang,
  ROUND(AVG(f.avg_value), 2)       AS aqi_trung_binh,
  COUNT(*)                         AS so_ban_ghi
FROM fact_air_quality f
  INNER JOIN dim_date d      ON f.date_key      = d.date_key
  INNER JOIN dim_pollutant p ON f.pollutant_key  = p.pollutant_key
WHERE p.pollutant_code = 'aqi'
GROUP BY d.year, d.quarter, d.month WITH ROLLUP;"
            },
            new OlapQueryResult
            {
                QueryName = "Q2_Rollup_DiaLy",
                BusinessQuestion = "AQI trung bình theo từng thành phố và tỉnh, cuộn lên từ Thành phố → Tỉnh → Tổng thể?",
                OlapOperation = "ROLL-UP (GROUP BY ... WITH ROLLUP)",
                SqlText = @"SELECT
  l.province                       AS tinh,
  l.city                           AS thanh_pho,
  ROUND(AVG(f.avg_value), 2)       AS aqi_trung_binh,
  COUNT(*)                         AS so_ban_ghi
FROM fact_air_quality f
  INNER JOIN dim_location  l ON f.location_key  = l.location_key
  INNER JOIN dim_pollutant p ON f.pollutant_key = p.pollutant_key
WHERE p.pollutant_code = 'aqi'
GROUP BY l.province, l.city WITH ROLLUP;"
            },
            new OlapQueryResult
            {
                QueryName = "Q3_Drilldown_HaNoi",
                BusinessQuestion = "Chi tiết AQI tại Hà Nội ở từng mức độ phân giải thời gian (năm, quý, tháng, ngày)?",
                OlapOperation = "DRILL-DOWN (UNION ALL 4 mức)",
                SqlText = @"SELECT
  'Năm'                            AS muc_do,
  d.year                           AS nam,
  NULL                             AS quy,
  NULL                             AS thang,
  NULL                             AS ngay,
  ROUND(AVG(f.avg_value), 2)       AS aqi_trung_binh,
  COUNT(*)                         AS so_ban_ghi
FROM fact_air_quality f
  INNER JOIN dim_date      d ON f.date_key      = d.date_key
  INNER JOIN dim_location  l ON f.location_key  = l.location_key
  INNER JOIN dim_pollutant p ON f.pollutant_key = p.pollutant_key
WHERE l.city = 'Hà Nội' AND p.pollutant_code = 'aqi'
GROUP BY d.year

UNION ALL

SELECT
  'Quý'                            AS muc_do,
  d.year                           AS nam,
  d.quarter                        AS quy,
  NULL                             AS thang,
  NULL                             AS ngay,
  ROUND(AVG(f.avg_value), 2)       AS aqi_trung_binh,
  COUNT(*)                         AS so_ban_ghi
FROM fact_air_quality f
  INNER JOIN dim_date      d ON f.date_key      = d.date_key
  INNER JOIN dim_location  l ON f.location_key  = l.location_key
  INNER JOIN dim_pollutant p ON f.pollutant_key = p.pollutant_key
WHERE l.city = 'Hà Nội' AND p.pollutant_code = 'aqi'
GROUP BY d.year, d.quarter

UNION ALL

SELECT
  'Tháng'                          AS muc_do,
  d.year                           AS nam,
  d.quarter                        AS quy,
  d.month                          AS thang,
  NULL                             AS ngay,
  ROUND(AVG(f.avg_value), 2)       AS aqi_trung_binh,
  COUNT(*)                         AS so_ban_ghi
FROM fact_air_quality f
  INNER JOIN dim_date      d ON f.date_key      = d.date_key
  INNER JOIN dim_location  l ON f.location_key  = l.location_key
  INNER JOIN dim_pollutant p ON f.pollutant_key = p.pollutant_key
WHERE l.city = 'Hà Nội' AND p.pollutant_code = 'aqi'
GROUP BY d.year, d.quarter, d.month

UNION ALL

SELECT
  'Ngày'                           AS muc_do,
  d.year                           AS nam,
  d.quarter                        AS quy,
  d.month                          AS thang,
  d.full_date                      AS ngay,
  ROUND(AVG(f.avg_value), 2)       AS aqi_trung_binh,
  COUNT(*)                         AS so_ban_ghi
FROM fact_air_quality f
  INNER JOIN dim_date      d ON f.date_key      = d.date_key
  INNER JOIN dim_location  l ON f.location_key  = l.location_key
  INNER JOIN dim_pollutant p ON f.pollutant_key = p.pollutant_key
WHERE l.city = 'Hà Nội' AND p.pollutant_code = 'aqi'
GROUP BY d.year, d.quarter, d.month, d.full_date

ORDER BY muc_do, nam, quy, thang, ngay;"
            },
            new OlapQueryResult
            {
                QueryName = "Q4_Slice_HaNoi",
                BusinessQuestion = "AQI trung bình theo từng tháng tại Hà Nội diễn biến ra sao?",
                OlapOperation = "SLICE (cố định thành phố = 'Hà Nội')",
                SqlText = @"SELECT
  d.year                           AS nam,
  d.month                          AS thang,
  l.city                           AS thanh_pho,
  ROUND(AVG(f.avg_value), 2)       AS aqi_trung_binh,
  ROUND(MIN(f.min_value), 2)       AS aqi_nho_nhat,
  ROUND(MAX(f.max_value), 2)       AS aqi_lon_nhat,
  COUNT(*)                         AS so_ban_ghi
FROM fact_air_quality f
  INNER JOIN dim_date      d ON f.date_key      = d.date_key
  INNER JOIN dim_location  l ON f.location_key  = l.location_key
  INNER JOIN dim_pollutant p ON f.pollutant_key = p.pollutant_key
WHERE l.city = 'Hà Nội'
  AND p.pollutant_code = 'aqi'
GROUP BY d.year, d.month, l.city
ORDER BY d.year, d.month;"
            },
            new OlapQueryResult
            {
                QueryName = "Q5_Dice_Hanoi_HCM_Pollutants",
                BusinessQuestion = "So sánh AQI và PM2.5 giữa Hà Nội và Hồ Chí Minh trong năm 2020 và 2021?",
                OlapOperation = "DICE (lọc đồng thời trên 3 chiều: thời gian, địa điểm, chất ô nhiễm)",
                SqlText = @"SELECT
  d.year                           AS nam,
  l.city                           AS thanh_pho,
  p.pollutant_code                 AS ma_chat,
  ROUND(AVG(f.avg_value), 2)       AS gia_tri_trung_binh,
  ROUND(MIN(f.min_value), 2)       AS gia_tri_nho_nhat,
  ROUND(MAX(f.max_value), 2)       AS gia_tri_lon_nhat,
  COUNT(*)                         AS so_ban_ghi
FROM fact_air_quality f
  INNER JOIN dim_date      d ON f.date_key      = d.date_key
  INNER JOIN dim_location  l ON f.location_key  = l.location_key
  INNER JOIN dim_pollutant p ON f.pollutant_key = p.pollutant_key
WHERE d.year IN (2020, 2021)
  AND l.city IN ('Hà Nội', 'Hồ Chí Minh')
  AND p.pollutant_code IN ('pm25', 'aqi')
GROUP BY d.year, l.city, p.pollutant_code
ORDER BY d.year, l.city, p.pollutant_code;"
            },
            new OlapQueryResult
            {
                QueryName = "Q6_Pivot_City_Month",
                BusinessQuestion = "Bảng chéo AQI trung bình hàng tháng của các thành phố chính là bao nhiêu?",
                OlapOperation = "PIVOT (xoay dữ liệu các thành phố thành cột)",
                SqlText = @"SELECT
  d.year                           AS nam,
  d.month                          AS thang,
  ROUND(
    SUM(CASE WHEN l.city = 'Hà Nội'       THEN f.avg_value END) /
    NULLIF(SUM(CASE WHEN l.city = 'Hà Nội' THEN 1 ELSE 0 END), 0),
  2)                               AS ha_noi,
  ROUND(
    SUM(CASE WHEN l.city = 'Hồ Chí Minh'       THEN f.avg_value END) /
    NULLIF(SUM(CASE WHEN l.city = 'Hồ Chí Minh' THEN 1 ELSE 0 END), 0),
  2)                               AS ho_chi_minh,
  ROUND(
    SUM(CASE WHEN l.city = 'Đà Nẵng'       THEN f.avg_value END) /
    NULLIF(SUM(CASE WHEN l.city = 'Đà Nẵng' THEN 1 ELSE 0 END), 0),
  2)                               AS da_nang,
  ROUND(
    SUM(CASE WHEN l.city = 'Hải Phòng'       THEN f.avg_value END) /
    NULLIF(SUM(CASE WHEN l.city = 'Hải Phòng' THEN 1 ELSE 0 END), 0),
  2)                               AS hai_phong
FROM fact_air_quality f
  INNER JOIN dim_date      d ON f.date_key      = d.date_key
  INNER JOIN dim_location  l ON f.location_key  = l.location_key
  INNER JOIN dim_pollutant p ON f.pollutant_key = p.pollutant_key
WHERE p.pollutant_code = 'aqi'
GROUP BY d.year, d.month
ORDER BY d.year, d.month;"
            },
            new OlapQueryResult
            {
                QueryName = "Q7_Top5_O_Nhiem_Nhat",
                BusinessQuestion = "5 thành phố nào có chỉ số AQI trung bình cao nhất, và bao nhiêu ngày AQI vượt 100?",
                OlapOperation = "RANKING (Top-N)",
                SqlText = @"SELECT
  l.city                           AS thanh_pho,
  ROUND(AVG(f.avg_value), 2)       AS aqi_trung_binh,
  MAX(f.max_value)                 AS aqi_cao_nhat,
  COUNT(*)                         AS tong_so_ngay,
  SUM(CASE WHEN f.avg_value > 100 THEN 1 ELSE 0 END)
                                   AS so_ngay_vuot_100
FROM fact_air_quality f
  INNER JOIN dim_location  l ON f.location_key  = l.location_key
  INNER JOIN dim_pollutant p ON f.pollutant_key = p.pollutant_key
WHERE p.pollutant_code = 'aqi'
GROUP BY l.city
ORDER BY aqi_trung_binh DESC
LIMIT 5;"
            },
            new OlapQueryResult
            {
                QueryName = "Q8_SoSanh_2020_vs_2021",
                BusinessQuestion = "AQI trung bình, AQI cao nhất và số ngày vượt 100 của từng thành phố thay đổi ra sao giữa 2020 và 2021?",
                OlapOperation = "SO SÁNH LIÊN KỲ (Conditional Aggregation)",
                SqlText = @"SELECT
  l.city                           AS thanh_pho,
  ROUND(AVG(CASE WHEN d.year = 2020 THEN f.avg_value END), 2)
                                   AS aqi_tb_2020,
  MAX(CASE WHEN d.year = 2020 THEN f.max_value END)
                                   AS aqi_max_2020,
  SUM(CASE WHEN d.year = 2020 AND f.avg_value > 100 THEN 1 ELSE 0 END)
                                   AS ngay_vuot_100_2020,
  ROUND(AVG(CASE WHEN d.year = 2021 THEN f.avg_value END), 2)
                                   AS aqi_tb_2021,
  MAX(CASE WHEN d.year = 2021 THEN f.max_value END)
                                   AS aqi_max_2021,
  SUM(CASE WHEN d.year = 2021 AND f.avg_value > 100 THEN 1 ELSE 0 END)
                                   AS ngay_vuot_100_2021,
  ROUND(
    AVG(CASE WHEN d.year = 2021 THEN f.avg_value END) -
    AVG(CASE WHEN d.year = 2020 THEN f.avg_value END),
  2)                               AS chenh_lech_aqi
FROM fact_air_quality f
  INNER JOIN dim_date      d ON f.date_key      = d.date_key
  INNER JOIN dim_location  l ON f.location_key  = l.location_key
  INNER JOIN dim_pollutant p ON f.pollutant_key = p.pollutant_key
WHERE p.pollutant_code = 'aqi'
  AND d.year IN (2020, 2021)
GROUP BY l.city
ORDER BY chenh_lech_aqi DESC;"
            }
        };
    }

    /// <summary>
    /// In xem trước tối đa 5 dòng của DataTable lên màn hình Console
    /// </summary>
    private static void PrintDataTablePreview(DataTable dt)
    {
        if (dt.Rows.Count == 0)
        {
            Console.WriteLine("   (Không có dữ liệu trả về)");
            return;
        }

        var colNames = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        Console.WriteLine("   " + string.Join(" | ", colNames));
        Console.WriteLine("   " + new string('-', Math.Min(100, colNames.Sum(c => c.Length + 3))));

        int previewRows = Math.Min(5, dt.Rows.Count);
        for (int i = 0; i < previewRows; i++)
        {
            var rowValues = dt.Rows[i].ItemArray.Select(v => v == DBNull.Value ? "NULL" : v?.ToString());
            Console.WriteLine("   " + string.Join(" | ", rowValues));
        }

        if (dt.Rows.Count > 5)
        {
            Console.WriteLine($"   ... và {dt.Rows.Count - 5} dòng khác.");
        }
    }
}
