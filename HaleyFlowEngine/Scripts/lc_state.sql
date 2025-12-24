-- --------------------------------------------------------
-- Host:                         127.0.0.1
-- Server version:               11.8.2-MariaDB - mariadb.org binary distribution
-- Server OS:                    Win64
-- HeidiSQL Version:             12.10.0.7000
-- --------------------------------------------------------

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET NAMES utf8 */;
/*!50503 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;


-- Dumping database structure for lcstate
CREATE DATABASE IF NOT EXISTS `lcstate` /*!40100 DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci */;
USE `lcstate`;

-- Dumping structure for table lcstate.ack_log
CREATE TABLE IF NOT EXISTS `ack_log` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `ack_status` int(11) NOT NULL DEFAULT 1 COMMENT 'Flag:\n1 = Pending\n2 = Delivered\n3 = Processed (Idempotent)\n4 = Failed',
  `last_retry` datetime NOT NULL DEFAULT current_timestamp(),
  `retry_count` int(11) NOT NULL DEFAULT 0,
  `created` datetime NOT NULL DEFAULT current_timestamp(),
  `modified` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `message_id` char(36) NOT NULL DEFAULT uuid() COMMENT 'Each consumer can have its own message id. When a transition happens, we can easily track what each consumer is doing with that transition.',
  `transition_log` bigint(20) NOT NULL,
  `consumer` int(11) NOT NULL DEFAULT 0 COMMENT 'consumer of this acknowledgement',
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_ack_log` (`consumer`,`transition_log`),
  UNIQUE KEY `unq_ack_log_0` (`message_id`),
  KEY `fk_ack_log_transition_log` (`transition_log`),
  CONSTRAINT `fk_ack_log_transition_log` FOREIGN KEY (`transition_log`) REFERENCES `transition_log` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.category
CREATE TABLE IF NOT EXISTS `category` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `display_name` varchar(120) NOT NULL,
  `name` varchar(120) GENERATED ALWAYS AS (lcase(trim(`display_name`))) STORED,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_category` (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.definition
CREATE TABLE IF NOT EXISTS `definition` (
  `id` int(11) NOT NULL AUTO_INCREMENT COMMENT 'it should be a code provided by the user.',
  `env` int(11) NOT NULL DEFAULT 0,
  `guid` char(36) NOT NULL DEFAULT uuid(),
  `display_name` varchar(200) NOT NULL,
  `name` varchar(200) GENERATED ALWAYS AS (lcase(trim(`display_name`))) STORED,
  `description` text DEFAULT NULL,
  `created` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_definition_0` (`guid`),
  UNIQUE KEY `unq_definition` (`env`,`name`),
  CONSTRAINT `fk_definition_environment` FOREIGN KEY (`env`) REFERENCES `environment` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB AUTO_INCREMENT=1998 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.def_version
CREATE TABLE IF NOT EXISTS `def_version` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `guid` char(36) NOT NULL DEFAULT uuid(),
  `version` int(11) NOT NULL DEFAULT 1,
  `created` datetime NOT NULL DEFAULT current_timestamp(),
  `modified` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `parent` int(11) NOT NULL,
  `data` longtext NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_def_version` (`parent`,`version`),
  UNIQUE KEY `unq_def_version_0` (`guid`),
  KEY `fk_def_version_definition` (`parent`),
  CONSTRAINT `fk_def_version_definition` FOREIGN KEY (`parent`) REFERENCES `definition` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `cns_def_version` CHECK (`version` > 0),
  CONSTRAINT `cns_def_version_0` CHECK (json_valid(`data`))
) ENGINE=InnoDB AUTO_INCREMENT=1990 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.environment
CREATE TABLE IF NOT EXISTS `environment` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `display_name` varchar(120) NOT NULL,
  `name` varchar(120) GENERATED ALWAYS AS (lcase(trim(`display_name`))) STORED,
  `code` int(11) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_environment` (`code`),
  UNIQUE KEY `unq_environment_0` (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='environment code\n//Doesnt'' need to be like dev/prod/test.. It can be an work-group environmetn as well..\n\n//like preq-app (is one environment), so all preq-app (wherever it runs, local, production etc) will be able to read definitions.\n\n//we can even extend it as , preq-app-dev, preq-app-prod etc.';

-- Data exporting was unselected.

-- Dumping structure for table lcstate.events
CREATE TABLE IF NOT EXISTS `events` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `display_name` varchar(120) NOT NULL,
  `code` int(11) NOT NULL,
  `name` varchar(120) GENERATED ALWAYS AS (lcase(trim(`display_name`))) STORED,
  `def_version` int(11) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_events_0` (`def_version`,`code`),
  UNIQUE KEY `unq_events` (`def_version`,`code`,`name`),
  CONSTRAINT `fk_events_def_version` FOREIGN KEY (`def_version`) REFERENCES `def_version` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.instance
CREATE TABLE IF NOT EXISTS `instance` (
  `current_state` int(11) NOT NULL,
  `last_event` int(11) DEFAULT NULL,
  `guid` char(36) NOT NULL DEFAULT uuid(),
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `flags` int(10) unsigned NOT NULL DEFAULT 0 COMMENT 'active =1,\nsuspended =2 ,\ncompleted = 4,\nfailed = 8, \narchive = 16',
  `def_version` int(11) NOT NULL,
  `external_ref` char(36) DEFAULT NULL COMMENT 'like external workflow id or submission id or transmittal id.. Expected value is a GUID',
  `created` datetime NOT NULL DEFAULT current_timestamp(),
  `modified` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_instance` (`guid`),
  UNIQUE KEY `unq_instance_0` (`def_version`,`external_ref`),
  KEY `fk_instance_state` (`current_state`),
  KEY `fk_instance_events` (`last_event`),
  KEY `fk_instance_def_version` (`def_version`),
  KEY `idx_instance` (`external_ref`),
  CONSTRAINT `fk_instance_def_version` FOREIGN KEY (`def_version`) REFERENCES `def_version` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_instance_events` FOREIGN KEY (`last_event`) REFERENCES `events` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_instance_state` FOREIGN KEY (`current_state`) REFERENCES `state` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.state
CREATE TABLE IF NOT EXISTS `state` (
  `category` int(11) NOT NULL DEFAULT 0,
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `display_name` varchar(200) NOT NULL,
  `name` varchar(200) GENERATED ALWAYS AS (lcase(trim(`display_name`))) STORED,
  `flags` int(10) unsigned NOT NULL DEFAULT 0 COMMENT 'none = 0\nis_initial = 1\nis_final = 2\nis_system = 4\nis_error = 8',
  `created` datetime NOT NULL DEFAULT current_timestamp(),
  `def_version` int(11) NOT NULL,
  `timeout_minutes` int(11) DEFAULT NULL COMMENT 'in minutes',
  `timeout_mode` int(11) NOT NULL DEFAULT 0 COMMENT '0 = Once\n1 = Repeat',
  `timeout_event` int(11) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_state` (`def_version`,`name`),
  KEY `fk_state_category` (`category`),
  KEY `fk_state_events` (`timeout_event`),
  CONSTRAINT `fk_state_category` FOREIGN KEY (`category`) REFERENCES `category` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_state_def_version` FOREIGN KEY (`def_version`) REFERENCES `def_version` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_state_events` FOREIGN KEY (`timeout_event`) REFERENCES `events` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB AUTO_INCREMENT=2014 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.transition
CREATE TABLE IF NOT EXISTS `transition` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `from_state` int(11) NOT NULL,
  `to_state` int(11) NOT NULL,
  `created` datetime NOT NULL DEFAULT current_timestamp(),
  `def_version` int(11) NOT NULL,
  `event` int(11) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_transition` (`def_version`,`from_state`,`to_state`,`event`),
  KEY `fk_transition_state` (`from_state`),
  KEY `fk_transition_state_0` (`to_state`),
  KEY `fk_transition_events` (`event`),
  CONSTRAINT `fk_transition_def_version` FOREIGN KEY (`def_version`) REFERENCES `def_version` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_transition_events` FOREIGN KEY (`event`) REFERENCES `events` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_transition_state` FOREIGN KEY (`from_state`) REFERENCES `state` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_transition_state_0` FOREIGN KEY (`to_state`) REFERENCES `state` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.transition_data
CREATE TABLE IF NOT EXISTS `transition_data` (
  `transition_log` bigint(20) NOT NULL,
  `actor` varchar(255) DEFAULT NULL,
  `metadata` longtext DEFAULT NULL COMMENT 'Could be any data that was the result of this transition (which could be later used as a reference or  input for other items)',
  PRIMARY KEY (`transition_log`),
  CONSTRAINT `fk_transition_data_transition_log` FOREIGN KEY (`transition_log`) REFERENCES `transition_log` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.transition_log
CREATE TABLE IF NOT EXISTS `transition_log` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `instance_id` bigint(20) NOT NULL,
  `from_state` int(11) NOT NULL,
  `to_state` int(11) NOT NULL,
  `event` int(11) NOT NULL,
  `created` datetime NOT NULL DEFAULT utc_timestamp(),
  PRIMARY KEY (`id`),
  KEY `fk_transition_log_instance` (`instance_id`),
  CONSTRAINT `fk_transition_log_instance` FOREIGN KEY (`instance_id`) REFERENCES `instance` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

/*!40103 SET TIME_ZONE=IFNULL(@OLD_TIME_ZONE, 'system') */;
/*!40101 SET SQL_MODE=IFNULL(@OLD_SQL_MODE, '') */;
/*!40014 SET FOREIGN_KEY_CHECKS=IFNULL(@OLD_FOREIGN_KEY_CHECKS, 1) */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40111 SET SQL_NOTES=IFNULL(@OLD_SQL_NOTES, 1) */;
