-- ============================================================
-- Tệp: 02_create_dw.sql
-- Mô tả: Tạo các bảng chiều (dimension) và bảng sự kiện (fact)
--         cho kho dữ liệu chất lượng không khí Việt Nam
-- Bộ ký tự: utf8mb4
-- ============================================================

USE air_quality_dw;

-- ============================================================
-- BẢNG CHIỀU THỜI GIAN (dim_date)
-- Lưu thông tin chi tiết về ngày tháng để phân tích theo thời gian
-- ============================================================
CREATE TABLE IF NOT EXISTS dim_date (
  date_key INT PRIMARY KEY                 COMMENT 'Khóa ngày (định dạng YYYYMMDD)',
  full_date DATE NOT NULL                  COMMENT 'Ngày đầy đủ',
  day TINYINT                              COMMENT 'Ngày trong tháng (1-31)',
  month TINYINT                            COMMENT 'Tháng trong năm (1-12)',
  quarter TINYINT                          COMMENT 'Quý trong năm (1-4)',
  year SMALLINT                            COMMENT 'Năm',
  day_of_week TINYINT                      COMMENT 'Ngày trong tuần (1=Chủ nhật..7=Thứ bảy)',
  day_name VARCHAR(20)                     COMMENT 'Tên ngày trong tuần',
  month_name VARCHAR(20)                   COMMENT 'Tên tháng',
  is_weekend TINYINT                       COMMENT 'Có phải cuối tuần không (1=Có, 0=Không)',
  UNIQUE INDEX uix_full_date (full_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
  COMMENT='Bảng chiều thời gian';

-- Chèn dòng "Không xác định" cho các khóa ngoại không khớp
INSERT INTO dim_date (date_key, full_date, day, month, quarter, year,
                      day_of_week, day_name, month_name, is_weekend)
VALUES (-1, '1900-01-01', 1, 1, 1, 1900, 1, 'Unknown', 'Unknown', 0)
ON DUPLICATE KEY UPDATE date_key = date_key;

-- ------------------------------------------------------------
-- Thủ tục nạp tất cả các ngày từ 2019-12-30 đến 2021-12-31
-- Sử dụng CTE đệ quy để sinh dãy ngày
-- ------------------------------------------------------------
DROP PROCEDURE IF EXISTS sp_populate_dim_date;

DELIMITER $$
CREATE PROCEDURE sp_populate_dim_date()
BEGIN
  -- Chèn tất cả ngày từ 2019-12-30 đến 2021-12-31 bằng CTE đệ quy
  INSERT IGNORE INTO dim_date
    (date_key, full_date, day, month, quarter, year,
     day_of_week, day_name, month_name, is_weekend)
  WITH RECURSIVE date_series AS (
    -- Ngày bắt đầu
    SELECT DATE('2019-12-30') AS dt
    UNION ALL
    -- Tăng thêm 1 ngày cho đến ngày kết thúc
    SELECT DATE_ADD(dt, INTERVAL 1 DAY)
    FROM date_series
    WHERE dt < '2021-12-31'
  )
  SELECT
    -- Khóa ngày theo định dạng YYYYMMDD
    CAST(DATE_FORMAT(dt, '%Y%m%d') AS UNSIGNED) AS date_key,
    dt AS full_date,
    DAY(dt) AS day,
    MONTH(dt) AS month,
    QUARTER(dt) AS quarter,
    YEAR(dt) AS year,
    -- DAYOFWEEK: 1=Chủ nhật, 7=Thứ bảy
    DAYOFWEEK(dt) AS day_of_week,
    CASE DAYOFWEEK(dt)
      WHEN 1 THEN 'Chủ nhật'
      WHEN 2 THEN 'Thứ hai'
      WHEN 3 THEN 'Thứ ba'
      WHEN 4 THEN 'Thứ tư'
      WHEN 5 THEN 'Thứ năm'
      WHEN 6 THEN 'Thứ sáu'
      WHEN 7 THEN 'Thứ bảy'
    END AS day_name,
    CASE MONTH(dt)
      WHEN 1  THEN 'Tháng 1'
      WHEN 2  THEN 'Tháng 2'
      WHEN 3  THEN 'Tháng 3'
      WHEN 4  THEN 'Tháng 4'
      WHEN 5  THEN 'Tháng 5'
      WHEN 6  THEN 'Tháng 6'
      WHEN 7  THEN 'Tháng 7'
      WHEN 8  THEN 'Tháng 8'
      WHEN 9  THEN 'Tháng 9'
      WHEN 10 THEN 'Tháng 10'
      WHEN 11 THEN 'Tháng 11'
      WHEN 12 THEN 'Tháng 12'
    END AS month_name,
    -- Cuối tuần: Chủ nhật (1) hoặc Thứ bảy (7)
    CASE WHEN DAYOFWEEK(dt) IN (1, 7) THEN 1 ELSE 0 END AS is_weekend
  FROM date_series;
END$$
DELIMITER ;

-- Gọi thủ tục để nạp dữ liệu chiều thời gian
CALL sp_populate_dim_date();

-- ============================================================
-- BẢNG CHIỀU ĐỊA ĐIỂM (dim_location)
-- Lưu thông tin trạm quan trắc và vị trí địa lý
-- ============================================================
CREATE TABLE IF NOT EXISTS dim_location (
  location_key INT AUTO_INCREMENT PRIMARY KEY COMMENT 'Khóa chiều địa điểm',
  station_id VARCHAR(20)                   COMMENT 'Mã trạm quan trắc',
  station_name VARCHAR(500)                COMMENT 'Tên trạm quan trắc',
  city VARCHAR(100)                        COMMENT 'Tên thành phố',
  province VARCHAR(100)                    COMMENT 'Tên tỉnh/thành phố trực thuộc',
  latitude DECIMAL(10,6)                   COMMENT 'Vĩ độ',
  longitude DECIMAL(10,6)                  COMMENT 'Kinh độ',
  UNIQUE INDEX uix_station_city (station_id, city)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
  COMMENT='Bảng chiều địa điểm – trạm quan trắc';

-- Chèn dòng "Không xác định" với khóa cố định = -1
INSERT INTO dim_location (location_key, station_id, station_name, city, province,
                          latitude, longitude)
VALUES (-1, 'UNKNOWN', 'Không xác định', 'Unknown', 'Unknown', NULL, NULL)
ON DUPLICATE KEY UPDATE location_key = location_key;

-- ============================================================
-- BẢNG CHIỀU CHẤT Ô NHIỄM (dim_pollutant)
-- Lưu thông tin các loại chất ô nhiễm và thông số khí tượng
-- ============================================================
CREATE TABLE IF NOT EXISTS dim_pollutant (
  pollutant_key INT AUTO_INCREMENT PRIMARY KEY COMMENT 'Khóa chiều chất ô nhiễm',
  pollutant_code VARCHAR(20)               COMMENT 'Mã chất ô nhiễm',
  pollutant_name VARCHAR(100)              COMMENT 'Tên đầy đủ',
  unit VARCHAR(20)                         COMMENT 'Đơn vị đo',
  category VARCHAR(20)                     COMMENT 'Phân loại (ô nhiễm / khí tượng)',
  UNIQUE INDEX uix_pollutant_code (pollutant_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
  COMMENT='Bảng chiều chất ô nhiễm và thông số khí tượng';

-- Chèn dòng "Không xác định"
INSERT INTO dim_pollutant (pollutant_key, pollutant_code, pollutant_name, unit, category)
VALUES (-1, 'unknown', 'Không xác định', 'N/A', 'unknown')
ON DUPLICATE KEY UPDATE pollutant_key = pollutant_key;

-- Nạp sẵn tất cả các loại chất ô nhiễm và thông số khí tượng
INSERT INTO dim_pollutant (pollutant_code, pollutant_name, unit, category) VALUES
  ('aqi',           'Air Quality Index',  'index',   'pollutant'),
  ('pm25',          'Bụi mịn PM2.5',     'µg/m³',   'pollutant'),
  ('pm10',          'Bụi PM10',           'µg/m³',   'pollutant'),
  ('no2',           'Nitrogen Dioxide',   'µg/m³',   'pollutant'),
  ('co',            'Carbon Monoxide',    'mg/m³',   'pollutant'),
  ('o3',            'Ozone',              'µg/m³',   'pollutant'),
  ('so2',           'Sulfur Dioxide',     'µg/m³',   'pollutant'),
  ('temperature',   'Nhiệt độ',          '°C',      'meteorological'),
  ('humidity',      'Độ ẩm',             '%',       'meteorological'),
  ('dew',           'Điểm sương',        '°C',      'meteorological'),
  ('pressure',      'Áp suất',           'hPa',     'meteorological'),
  ('wind',          'Tốc độ gió',        'm/s',     'meteorological'),
  ('precipitation', 'Lượng mưa',         'mm',      'meteorological')
ON DUPLICATE KEY UPDATE pollutant_code = pollutant_code;

-- ============================================================
-- BẢNG CHIỀU MỨC AQI (dim_aqi_level)
-- Phân loại theo thang đo EPA Hoa Kỳ (6 mức)
-- ============================================================
CREATE TABLE IF NOT EXISTS dim_aqi_level (
  aqi_level_key INT PRIMARY KEY            COMMENT 'Khóa mức AQI (không tự tăng)',
  level_name VARCHAR(50)                   COMMENT 'Tên mức AQI (tiếng Anh)',
  min_aqi INT                              COMMENT 'Giá trị AQI tối thiểu',
  max_aqi INT                              COMMENT 'Giá trị AQI tối đa',
  color VARCHAR(20)                        COMMENT 'Màu đại diện (tiếng Việt)',
  health_implication VARCHAR(255)           COMMENT 'Ảnh hưởng sức khỏe (tiếng Việt)'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
  COMMENT='Bảng chiều mức chất lượng không khí theo thang EPA Hoa Kỳ';

-- Chèn dòng "Không xác định"
INSERT INTO dim_aqi_level (aqi_level_key, level_name, min_aqi, max_aqi, color, health_implication)
VALUES (-1, 'Unknown', NULL, NULL, 'Không rõ', 'Không xác định')
ON DUPLICATE KEY UPDATE aqi_level_key = aqi_level_key;

-- Nạp sẵn 6 mức AQI theo thang EPA
INSERT INTO dim_aqi_level (aqi_level_key, level_name, min_aqi, max_aqi, color, health_implication) VALUES
  (1, 'Good',                            0,   50,  'Xanh lá',  'Chất lượng không khí tốt'),
  (2, 'Moderate',                        51,  100, 'Vàng',     'Chấp nhận được'),
  (3, 'Unhealthy for Sensitive Groups',  101, 150, 'Cam',      'Nhóm nhạy cảm bị ảnh hưởng'),
  (4, 'Unhealthy',                       151, 200, 'Đỏ',       'Mọi người bắt đầu bị ảnh hưởng'),
  (5, 'Very Unhealthy',                  201, 300, 'Tím',      'Cảnh báo sức khỏe'),
  (6, 'Hazardous',                       301, 500, 'Nâu đỏ',   'Khẩn cấp sức khỏe')
ON DUPLICATE KEY UPDATE aqi_level_key = aqi_level_key;

-- ============================================================
-- BẢNG SỰ KIỆN CHẤT LƯỢNG KHÔNG KHÍ (fact_air_quality)
-- Bảng fact chính chứa các phép đo chất lượng không khí
-- Liên kết với tất cả bảng chiều qua khóa ngoại
-- ============================================================
CREATE TABLE IF NOT EXISTS fact_air_quality (
  fact_key BIGINT AUTO_INCREMENT PRIMARY KEY COMMENT 'Khóa chính tự tăng',
  date_key INT NOT NULL                    COMMENT 'Khóa ngoại → dim_date',
  location_key INT NOT NULL                COMMENT 'Khóa ngoại → dim_location',
  pollutant_key INT NOT NULL               COMMENT 'Khóa ngoại → dim_pollutant',
  aqi_level_key INT NOT NULL DEFAULT -1    COMMENT 'Khóa ngoại → dim_aqi_level',
  measurement_count INT                    COMMENT 'Số lần đo trong ngày',
  min_value DECIMAL(10,2)                  COMMENT 'Giá trị nhỏ nhất',
  max_value DECIMAL(10,2)                  COMMENT 'Giá trị lớn nhất',
  median_value DECIMAL(10,2)               COMMENT 'Giá trị trung vị',
  avg_value DECIMAL(10,2)                  COMMENT 'Giá trị trung bình',
  variance_value DECIMAL(12,4)             COMMENT 'Phương sai',
  source_file VARCHAR(100)                 COMMENT 'Tệp nguồn dữ liệu',

  -- Ràng buộc khóa ngoại
  CONSTRAINT fk_fact_date
    FOREIGN KEY (date_key) REFERENCES dim_date(date_key),
  CONSTRAINT fk_fact_location
    FOREIGN KEY (location_key) REFERENCES dim_location(location_key),
  CONSTRAINT fk_fact_pollutant
    FOREIGN KEY (pollutant_key) REFERENCES dim_pollutant(pollutant_key),
  CONSTRAINT fk_fact_aqi_level
    FOREIGN KEY (aqi_level_key) REFERENCES dim_aqi_level(aqi_level_key),

  -- Chỉ mục duy nhất đảm bảo không trùng lặp dữ liệu
  UNIQUE INDEX uix_fact (date_key, location_key, pollutant_key),

  -- Chỉ mục trên từng khóa ngoại để tối ưu truy vấn
  INDEX idx_fact_date (date_key),
  INDEX idx_fact_location (location_key),
  INDEX idx_fact_pollutant (pollutant_key),
  INDEX idx_fact_aqi_level (aqi_level_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
  COMMENT='Bảng sự kiện chính – phép đo chất lượng không khí';

-- ============================================================
-- BẢNG TỔNG HỢP THEO THÁNG VÀ THÀNH PHỐ (agg_monthly_city)
-- Lưu dữ liệu tổng hợp sẵn để tăng tốc báo cáo
-- ============================================================
CREATE TABLE IF NOT EXISTS agg_monthly_city (
  agg_key INT AUTO_INCREMENT PRIMARY KEY   COMMENT 'Khóa chính tự tăng',
  year SMALLINT                            COMMENT 'Năm',
  month TINYINT                            COMMENT 'Tháng',
  city VARCHAR(100)                        COMMENT 'Tên thành phố',
  pollutant_code VARCHAR(20)               COMMENT 'Mã chất ô nhiễm',
  avg_value DECIMAL(10,2)                  COMMENT 'Giá trị trung bình tháng',
  min_value DECIMAL(10,2)                  COMMENT 'Giá trị nhỏ nhất tháng',
  max_value DECIMAL(10,2)                  COMMENT 'Giá trị lớn nhất tháng',
  measurement_days INT                     COMMENT 'Số ngày có dữ liệu',
  days_above_100 INT DEFAULT 0             COMMENT 'Số ngày AQI vượt 100',

  -- Chỉ mục duy nhất theo tổ hợp năm-tháng-thành phố-chất ô nhiễm
  UNIQUE INDEX uix_agg_monthly (year, month, city, pollutant_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
  COMMENT='Bảng tổng hợp chất lượng không khí theo tháng và thành phố';
