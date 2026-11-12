-- ============================================================
-- Tệp: 03_olap_queries.sql
-- Mô tả: 8 truy vấn OLAP phân tích dữ liệu chất lượng không khí
--         Việt Nam (2020-2021)
-- Bộ ký tự: utf8mb4
-- ============================================================

USE air_quality_dw;

-- ============================================================
-- TRUY VẤN 1: Roll-up theo thời gian
-- Câu hỏi: Chỉ số AQI trung bình thay đổi như thế nào
--           khi cuộn lên từ Tháng → Quý → Năm?
-- Phép toán OLAP: ROLL-UP (GROUP BY ... WITH ROLLUP)
-- ============================================================
SELECT
  d.year                           AS nam,
  d.quarter                        AS quy,
  d.month                          AS thang,
  ROUND(AVG(f.avg_value), 2)       AS aqi_trung_binh,
  COUNT(*)                         AS so_ban_ghi
FROM fact_air_quality f
  INNER JOIN dim_date d      ON f.date_key      = d.date_key
  INNER JOIN dim_pollutant p ON f.pollutant_key  = p.pollutant_key
WHERE p.pollutant_code = 'aqi'
GROUP BY d.year, d.quarter, d.month WITH ROLLUP;

-- ============================================================
-- TRUY VẤN 2: Roll-up theo địa lý
-- Câu hỏi: AQI trung bình theo từng thành phố và tỉnh,
--           cuộn lên từ Thành phố → Tỉnh → Tổng thể?
-- Phép toán OLAP: ROLL-UP (GROUP BY ... WITH ROLLUP)
-- ============================================================
SELECT
  l.province                       AS tinh,
  l.city                           AS thanh_pho,
  ROUND(AVG(f.avg_value), 2)       AS aqi_trung_binh,
  COUNT(*)                         AS so_ban_ghi
FROM fact_air_quality f
  INNER JOIN dim_location  l ON f.location_key  = l.location_key
  INNER JOIN dim_pollutant p ON f.pollutant_key = p.pollutant_key
WHERE p.pollutant_code = 'aqi'
GROUP BY l.province, l.city WITH ROLLUP;

-- ============================================================
-- TRUY VẤN 3: Drill-down từ Năm → Quý → Tháng → Ngày
-- Câu hỏi: Chi tiết AQI tại Hà Nội ở từng mức độ
--           phân giải thời gian (năm, quý, tháng, ngày)?
-- Phép toán OLAP: DRILL-DOWN (UNION ALL 4 mức)
-- ============================================================

-- Mức 1: Theo năm
SELECT
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

-- Mức 2: Theo quý
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

-- Mức 3: Theo tháng
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

-- Mức 4: Theo ngày
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

ORDER BY muc_do, nam, quy, thang, ngay;

-- ============================================================
-- TRUY VẤN 4: Slice – Cố định 1 thành phố
-- Câu hỏi: AQI trung bình theo từng tháng tại Hà Nội
--           diễn biến ra sao?
-- Phép toán OLAP: SLICE (cố định chiều thành phố = 'Hà Nội')
-- ============================================================
SELECT
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
ORDER BY d.year, d.month;

-- ============================================================
-- TRUY VẤN 5: Dice – Lọc nhiều chiều đồng thời
-- Câu hỏi: So sánh AQI và PM2.5 giữa Hà Nội và
--           Hồ Chí Minh trong năm 2020 và 2021?
-- Phép toán OLAP: DICE (lọc đồng thời trên 3 chiều)
-- ============================================================
SELECT
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
ORDER BY d.year, l.city, p.pollutant_code;

-- ============================================================
-- TRUY VẤN 6: Pivot – Thành phố × Tháng
-- Câu hỏi: Bảng chéo AQI trung bình hàng tháng của
--           các thành phố chính là bao nhiêu?
-- Phép toán OLAP: PIVOT (xoay dữ liệu thành phố thành cột)
-- ============================================================
SELECT
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
ORDER BY d.year, d.month;

-- ============================================================
-- TRUY VẤN 7: Top N thành phố ô nhiễm nhất
-- Câu hỏi: 5 thành phố nào có chỉ số AQI trung bình
--           cao nhất, và bao nhiêu ngày AQI vượt 100?
-- Phép toán OLAP: RANKING (Top-N)
-- ============================================================
SELECT
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
LIMIT 5;

-- ============================================================
-- TRUY VẤN 8: So sánh năm 2020 và 2021
-- Câu hỏi: AQI trung bình, AQI cao nhất và số ngày vượt 100
--           của từng thành phố thay đổi ra sao giữa 2020 và 2021?
-- Phép toán OLAP: So sánh liên kỳ (tổng hợp có điều kiện)
-- ============================================================
SELECT
  l.city                           AS thanh_pho,
  -- Năm 2020
  ROUND(AVG(CASE WHEN d.year = 2020 THEN f.avg_value END), 2)
                                   AS aqi_tb_2020,
  MAX(CASE WHEN d.year = 2020 THEN f.max_value END)
                                   AS aqi_max_2020,
  SUM(CASE WHEN d.year = 2020 AND f.avg_value > 100 THEN 1 ELSE 0 END)
                                   AS ngay_vuot_100_2020,
  -- Năm 2021
  ROUND(AVG(CASE WHEN d.year = 2021 THEN f.avg_value END), 2)
                                   AS aqi_tb_2021,
  MAX(CASE WHEN d.year = 2021 THEN f.max_value END)
                                   AS aqi_max_2021,
  SUM(CASE WHEN d.year = 2021 AND f.avg_value > 100 THEN 1 ELSE 0 END)
                                   AS ngay_vuot_100_2021,
  -- Chênh lệch
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
ORDER BY chenh_lech_aqi DESC;
