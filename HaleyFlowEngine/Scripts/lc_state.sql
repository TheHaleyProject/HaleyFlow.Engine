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

-- Dumping structure for table lcstate.ack
CREATE TABLE IF NOT EXISTS `ack` (
  `guid` char(36) NOT NULL DEFAULT uuid(),
  `created` datetime NOT NULL DEFAULT current_timestamp(),
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_ack` (`guid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.ack_consumer
CREATE TABLE IF NOT EXISTS `ack_consumer` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `consumer` bigint(20) NOT NULL DEFAULT 0,
  `status` int(11) NOT NULL DEFAULT 1 COMMENT 'Flag:\n    Pending =1,\n    Delivered=2,\n    Processed=3,\n    Failed=4',
  `ack_id` bigint(20) NOT NULL,
  `last_trigger` datetime NOT NULL DEFAULT current_timestamp() COMMENT 'when was the last time, this trigger was initiated.. Whenever we send the ack to consumer, we mark it.',
  `trigger_count` int(11) NOT NULL DEFAULT 0 COMMENT 'how many times did we try to send this acknowledgement to the consumer',
  `next_due` datetime DEFAULT NULL,
  `created` datetime NOT NULL DEFAULT current_timestamp(),
  `modified` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_ack_consumer` (`ack_id`,`consumer`),
  KEY `idx_ack_consumer` (`next_due`,`status`),
  KEY `idx_ack_consumer_0` (`consumer`,`next_due`,`status`),
  CONSTRAINT `fk_ack_consumer_ack` FOREIGN KEY (`ack_id`) REFERENCES `ack` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.activity
CREATE TABLE IF NOT EXISTS `activity` (
  `display_name` varchar(140) NOT NULL,
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `name` varchar(140) GENERATED ALWAYS AS (lcase(trim(`display_name`))) STORED,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='These are minor applicatoin managed activies which the statemachine doens''t have any awareness about.. like, send_email, firstreview, escalatedreview, finalcheck, etc..';

-- Data exporting was unselected.

-- Dumping structure for table lcstate.activity_status
CREATE TABLE IF NOT EXISTS `activity_status` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `display_name` varchar(120) NOT NULL,
  `name` varchar(120) GENERATED ALWAYS AS (lcase(trim(`display_name`))) STORED,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='These are all execution or activity status, which WorkFlow engine has no visibility about.  Like, ''Pending''''Completed''"approved'',"Rejected''"Returned''.. Reason is, we dont know what kind of state each runtime activity might follow.. For instance, one of the runtime activity can have ''Approved'',''Rejected'' state.. another can only have ''Sent''"Pendin'' (like, email delivery)';

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
  `guid` char(36) NOT NULL DEFAULT uuid(),
  `display_name` varchar(200) NOT NULL,
  `name` varchar(200) GENERATED ALWAYS AS (lcase(trim(`display_name`))) STORED,
  `description` text DEFAULT NULL,
  `created` datetime NOT NULL DEFAULT current_timestamp(),
  `env` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_definition_0` (`guid`),
  UNIQUE KEY `unq_definition` (`env`,`name`),
  KEY `idx_definition` (`name`),
  CONSTRAINT `fk_definition_environment` FOREIGN KEY (`env`) REFERENCES `environment` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB AUTO_INCREMENT=1998 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.def_policies
CREATE TABLE IF NOT EXISTS `def_policies` (
  `definition` int(11) NOT NULL,
  `policy` int(11) NOT NULL,
  `modified` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`definition`,`policy`),
  KEY `fk_def_policies_policy` (`policy`),
  CONSTRAINT `fk_def_policies_definition` FOREIGN KEY (`definition`) REFERENCES `definition` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_def_policies_policy` FOREIGN KEY (`policy`) REFERENCES `policy` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.def_version
CREATE TABLE IF NOT EXISTS `def_version` (
  `guid` char(36) NOT NULL DEFAULT uuid(),
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `version` int(11) NOT NULL DEFAULT 1,
  `created` datetime NOT NULL DEFAULT current_timestamp(),
  `modified` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `parent` int(11) NOT NULL,
  `data` longtext NOT NULL,
  `hash` varchar(48) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_def_version` (`parent`,`version`),
  UNIQUE KEY `unq_def_version_0` (`guid`),
  UNIQUE KEY `unq_def_version_1` (`parent`,`hash`),
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
  `guid` varchar(42) NOT NULL DEFAULT uuid(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_environment` (`code`),
  UNIQUE KEY `unq_environment_1` (`guid`),
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

-- Dumping structure for table lcstate.hook
CREATE TABLE IF NOT EXISTS `hook` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `state_id` int(11) NOT NULL,
  `via_event` int(11) NOT NULL,
  `on_entry` bit(1) NOT NULL DEFAULT b'1' COMMENT 'by default, the hooks are for entry.. we can also, setup on leave.\n0 - on leaving\n1 - on entry',
  `route` varchar(180) NOT NULL COMMENT 'event or the route name that needs to be triggered or hooked.',
  `created` datetime NOT NULL DEFAULT current_timestamp(),
  `instance_id` bigint(20) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_hooks` (`instance_id`,`state_id`,`via_event`,`on_entry`,`route`),
  CONSTRAINT `fk_hooks_instance` FOREIGN KEY (`instance_id`) REFERENCES `instance` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='hooks are raised based on policy.. we just check the policy and then raise these hooks';

-- Data exporting was unselected.

-- Dumping structure for table lcstate.hook_ack
CREATE TABLE IF NOT EXISTS `hook_ack` (
  `ack_id` bigint(20) NOT NULL,
  `hook_id` bigint(20) NOT NULL,
  PRIMARY KEY (`hook_id`),
  UNIQUE KEY `unq_hook_ack` (`ack_id`),
  CONSTRAINT `fk_hook_ack_ack` FOREIGN KEY (`ack_id`) REFERENCES `ack` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_hook_ack_hook` FOREIGN KEY (`hook_id`) REFERENCES `hook` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.instance
CREATE TABLE IF NOT EXISTS `instance` (
  `current_state` int(11) NOT NULL DEFAULT 0,
  `last_event` int(11) DEFAULT NULL,
  `guid` char(36) NOT NULL DEFAULT uuid(),
  `policy_id` int(11) DEFAULT 0,
  `external_ref` char(36) DEFAULT NULL COMMENT 'like external workflow id or submission id or transmittal id.. Expected value is a GUID',
  `flags` int(10) unsigned NOT NULL DEFAULT 0 COMMENT 'active =1,\nsuspended =2 ,\ncompleted = 4,\nfailed = 8, \narchive = 16',
  `created` datetime NOT NULL DEFAULT current_timestamp(),
  `def_version` int(11) NOT NULL,
  `modified` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_instance` (`guid`),
  UNIQUE KEY `unq_instance_0` (`def_version`,`external_ref`),
  KEY `fk_instance_state` (`current_state`),
  KEY `fk_instance_events` (`last_event`),
  KEY `fk_instance_def_version` (`def_version`),
  KEY `idx_instance` (`external_ref`),
  CONSTRAINT `fk_instance_def_version` FOREIGN KEY (`def_version`) REFERENCES `def_version` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.lc_ack
CREATE TABLE IF NOT EXISTS `lc_ack` (
  `ack_id` bigint(20) NOT NULL,
  `lc_id` bigint(20) NOT NULL,
  PRIMARY KEY (`lc_id`),
  UNIQUE KEY `idx_lc_ack` (`ack_id`),
  CONSTRAINT `fk_lc_ack_ack` FOREIGN KEY (`ack_id`) REFERENCES `ack` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_lc_ack_lifecycle` FOREIGN KEY (`lc_id`) REFERENCES `lifecycle` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.lc_data
CREATE TABLE IF NOT EXISTS `lc_data` (
  `lc_id` bigint(20) NOT NULL,
  `actor` varchar(60) DEFAULT NULL,
  `payload` longtext DEFAULT NULL COMMENT 'Could be any data that was the result of this transition (which could be later used as a reference or  input for other items)',
  PRIMARY KEY (`lc_id`),
  CONSTRAINT `fk_transition_data_transition_log` FOREIGN KEY (`lc_id`) REFERENCES `lifecycle` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.lc_timeout
CREATE TABLE IF NOT EXISTS `lc_timeout` (
  `lc_id` bigint(20) NOT NULL,
  `created` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`lc_id`),
  CONSTRAINT `fk_timeout_lifecycle` FOREIGN KEY (`lc_id`) REFERENCES `lifecycle` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.lifecycle
CREATE TABLE IF NOT EXISTS `lifecycle` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `from_state` int(11) NOT NULL,
  `to_state` int(11) NOT NULL,
  `event` int(11) NOT NULL,
  `created` datetime NOT NULL DEFAULT current_timestamp(),
  `instance_id` bigint(20) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `fk_transition_log_instance` (`instance_id`),
  CONSTRAINT `fk_transition_log_instance` FOREIGN KEY (`instance_id`) REFERENCES `instance` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Contains the major states which are controlled by the statemachine';

-- Data exporting was unselected.

-- Dumping structure for table lcstate.policy
CREATE TABLE IF NOT EXISTS `policy` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `hash` varchar(48) NOT NULL COMMENT 'hash of the policy contents (states, attach modes, routes)',
  `content` text NOT NULL COMMENT 'supposedly the policy json',
  `created` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_policy` (`hash`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.runtime
CREATE TABLE IF NOT EXISTS `runtime` (
  `instance_id` bigint(20) NOT NULL,
  `activity` int(11) NOT NULL,
  `state_id` int(11) NOT NULL,
  `actor_id` varchar(60) NOT NULL DEFAULT '0',
  `status` int(11) NOT NULL,
  `created` datetime NOT NULL DEFAULT current_timestamp(),
  `modified` datetime NOT NULL DEFAULT current_timestamp(),
  `frozen` bit(1) NOT NULL DEFAULT b'0' COMMENT 'For instance, this specific item may have some status, but we might freeze it, meaning, it cannot change status anymore.. unless we unlock the frreeze state..',
  `lc_id` bigint(20) NOT NULL DEFAULT 0,
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_execution` (`instance_id`,`state_id`,`activity`,`actor_id`),
  KEY `fk_execution_runtime_state` (`status`),
  KEY `fk_runtime_activity` (`activity`),
  CONSTRAINT `fk_execution_runtime_state` FOREIGN KEY (`status`) REFERENCES `activity_status` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_runtime_activity` FOREIGN KEY (`activity`) REFERENCES `activity` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_runtime_instance` FOREIGN KEY (`instance_id`) REFERENCES `instance` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Remember, runtime state (or activity track) doesn''t need an acknowledgement, because, this infomration itself is managed in application side and we are only receiving it directly from the app itself.. But, there might be some events that we need to raise for each state based on the policy.. those has to be properly acknowledge.d';

-- Data exporting was unselected.

-- Dumping structure for table lcstate.runtime_data
CREATE TABLE IF NOT EXISTS `runtime_data` (
  `data` longtext DEFAULT NULL COMMENT 'data that needs to be displayed.. For instance, I can send in a json value, which can then be displayed in the UI with property name/value pair..  So that parsing during display can be reduced ..',
  `payload` longtext DEFAULT NULL COMMENT 'Some data associate with this transition.. may or may not be present, which can then be reused or used for idempotency.',
  `runtime` bigint(20) NOT NULL,
  PRIMARY KEY (`runtime`),
  CONSTRAINT `fk_runtime_data_runtime` FOREIGN KEY (`runtime`) REFERENCES `runtime` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Data exporting was unselected.

-- Dumping structure for table lcstate.state
CREATE TABLE IF NOT EXISTS `state` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `display_name` varchar(200) NOT NULL,
  `name` varchar(200) GENERATED ALWAYS AS (lcase(trim(`display_name`))) STORED,
  `flags` int(10) unsigned NOT NULL DEFAULT 0 COMMENT 'none = 0\nis_initial = 1\nis_final = 2\nis_system = 4\nis_error = 8',
  `created` datetime NOT NULL DEFAULT current_timestamp(),
  `timeout_minutes` int(11) DEFAULT NULL COMMENT 'in minutes',
  `timeout_mode` int(11) NOT NULL DEFAULT 0 COMMENT '0 = Once\n1 = Repeat',
  `timeout_event` int(11) DEFAULT NULL,
  `def_version` int(11) NOT NULL,
  `category` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unq_state` (`def_version`,`name`),
  KEY `fk_state_category` (`category`),
  KEY `fk_state_events` (`timeout_event`),
  CONSTRAINT `fk_state_category` FOREIGN KEY (`category`) REFERENCES `category` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_state_def_version` FOREIGN KEY (`def_version`) REFERENCES `def_version` (`id`) ON DELETE NO ACTION ON UPDATE NO ACTION
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

/*!40103 SET TIME_ZONE=IFNULL(@OLD_TIME_ZONE, 'system') */;
/*!40101 SET SQL_MODE=IFNULL(@OLD_SQL_MODE, '') */;
/*!40014 SET FOREIGN_KEY_CHECKS=IFNULL(@OLD_FOREIGN_KEY_CHECKS, 1) */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40111 SET SQL_NOTES=IFNULL(@OLD_SQL_NOTES, 1) */;
