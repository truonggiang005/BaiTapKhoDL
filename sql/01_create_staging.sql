-- ============================================================
-- Tệp: 01_create_staging.sql
-- Mô tả: Tạo cơ sở dữ liệu và các bảng staging cho dữ liệu
--         chất lượng không khí Việt Nam 2020-2021
-- Bộ ký tự: utf8mb4
-- ============================================================

-- Tạo cơ sở dữ liệu nếu chưa tồn tại
CREATE DATABASE IF NOT EXISTS air_quality_dw
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

USE air_quality_dw;

-- ------------------------------------------------------------
-- Bảng staging cho dữ liệu năm 2020
-- Chứa dữ liệu thô từ tệp CSV gốc năm 2020
-- Tất cả cột dùng VARCHAR để chấp nhận mọi giá trị thô
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS staging_2020 (
  id INT AUTO_INCREMENT PRIMARY KEY,
  raw_date VARCHAR(20)                     COMMENT 'Ngày đo (dạng chuỗi thô)',
  country VARCHAR(10)                      COMMENT 'Mã quốc gia',
  city VARCHAR(100)                        COMMENT 'Tên thành phố',
  specie VARCHAR(50)                       COMMENT 'Loại chất ô nhiễm',
  measurement_count VARCHAR(20)            COMMENT 'Số lần đo',
  min_value VARCHAR(20)                    COMMENT 'Giá trị nhỏ nhất',
  max_value VARCHAR(20)                    COMMENT 'Giá trị lớn nhất',
  median_value VARCHAR(20)                 COMMENT 'Giá trị trung vị',
  variance_value VARCHAR(20)              COMMENT 'Phương sai',
  source_file VARCHAR(255)                 COMMENT 'Tên tệp nguồn',
  loaded_at DATETIME DEFAULT CURRENT_TIMESTAMP COMMENT 'Thời điểm nạp dữ liệu'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
  COMMENT='Bảng staging chứa dữ liệu thô chất lượng không khí năm 2020';

-- ------------------------------------------------------------
-- Bảng staging cho dữ liệu năm 2021
-- Chứa dữ liệu thô từ tệp CSV gốc năm 2021
-- Cấu trúc khác năm 2020 vì nguồn dữ liệu khác nhau
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS staging_2021 (
  id INT AUTO_INCREMENT PRIMARY KEY,
  station_id VARCHAR(20)                   COMMENT 'Mã trạm quan trắc',
  aqi_index VARCHAR(20)                    COMMENT 'Chỉ số AQI',
  location VARCHAR(100)                    COMMENT 'Vị trí trạm',
  station_name VARCHAR(500)                COMMENT 'Tên trạm quan trắc',
  url VARCHAR(500)                         COMMENT 'Đường dẫn nguồn dữ liệu',
  dominant_pollutant VARCHAR(20)           COMMENT 'Chất ô nhiễm chủ đạo',
  co VARCHAR(20)                           COMMENT 'Nồng độ CO',
  dew VARCHAR(20)                          COMMENT 'Điểm sương',
  humidity VARCHAR(20)                     COMMENT 'Độ ẩm',
  no2 VARCHAR(20)                          COMMENT 'Nồng độ NO₂',
  o3 VARCHAR(20)                           COMMENT 'Nồng độ O₃',
  pressure VARCHAR(20)                     COMMENT 'Áp suất khí quyển',
  pm10 VARCHAR(20)                         COMMENT 'Nồng độ bụi PM10',
  pm25 VARCHAR(20)                         COMMENT 'Nồng độ bụi mịn PM2.5',
  so2 VARCHAR(20)                          COMMENT 'Nồng độ SO₂',
  temperature VARCHAR(20)                  COMMENT 'Nhiệt độ',
  wind VARCHAR(20)                         COMMENT 'Tốc độ gió',
  data_time VARCHAR(30)                    COMMENT 'Thời điểm đo (dạng chuỗi)',
  data_time_tz VARCHAR(10)                 COMMENT 'Múi giờ',
  status VARCHAR(100)                      COMMENT 'Trạng thái dữ liệu',
  alert_level VARCHAR(20)                  COMMENT 'Mức cảnh báo',
  source_file VARCHAR(255)                 COMMENT 'Tên tệp nguồn',
  loaded_at DATETIME DEFAULT CURRENT_TIMESTAMP COMMENT 'Thời điểm nạp dữ liệu'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
  COMMENT='Bảng staging chứa dữ liệu thô chất lượng không khí năm 2021';

-- ------------------------------------------------------------
-- Bảng ghi nhật ký quá trình ETL
-- Theo dõi từng bước chạy ETL: số dòng đọc, nạp, loại bỏ
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS etl_log (
  log_id INT AUTO_INCREMENT PRIMARY KEY,
  run_id VARCHAR(50)                       COMMENT 'Mã lần chạy ETL',
  run_timestamp DATETIME DEFAULT CURRENT_TIMESTAMP COMMENT 'Thời điểm chạy',
  source_file VARCHAR(255)                 COMMENT 'Tệp nguồn được xử lý',
  step_name VARCHAR(50)                    COMMENT 'Tên bước xử lý',
  rows_read INT DEFAULT 0                  COMMENT 'Số dòng đã đọc',
  rows_loaded INT DEFAULT 0                COMMENT 'Số dòng đã nạp thành công',
  rows_rejected INT DEFAULT 0              COMMENT 'Số dòng bị loại bỏ',
  duration_ms INT DEFAULT 0                COMMENT 'Thời gian xử lý (mili giây)',
  notes TEXT                               COMMENT 'Ghi chú bổ sung'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
  COMMENT='Bảng nhật ký theo dõi quá trình ETL';

-- ------------------------------------------------------------
-- Bảng ghi chi tiết các dòng bị loại bỏ trong quá trình ETL
-- Giúp truy vết nguyên nhân dữ liệu bị từ chối
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS etl_reject_log (
  reject_id INT AUTO_INCREMENT PRIMARY KEY,
  run_id VARCHAR(50)                       COMMENT 'Mã lần chạy ETL',
  source_file VARCHAR(255)                 COMMENT 'Tệp nguồn',
  `row_number` INT                         COMMENT 'Số thứ tự dòng bị loại',
  column_name VARCHAR(50)                  COMMENT 'Tên cột gây lỗi',
  raw_value TEXT                           COMMENT 'Giá trị thô bị lỗi',
  reject_reason VARCHAR(255)               COMMENT 'Lý do loại bỏ',
  created_at DATETIME DEFAULT CURRENT_TIMESTAMP COMMENT 'Thời điểm ghi nhận'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
  COMMENT='Bảng ghi chi tiết các dòng bị từ chối trong quá trình ETL';
