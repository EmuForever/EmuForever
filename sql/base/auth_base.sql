-- CypherCore (Forever 1.60.1.70425) auth base database
-- Schema for every auth table plus seed data for build info, RBAC and realmlist.
-- Battle.not tables use the bnot_ prefix.
-- Usage: mysql -uroot < sql/base/auth_base.sql

CREATE DATABASE IF NOT EXISTS `auth` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE `auth`;

SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0;
SET NAMES utf8mb4;

/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;
DROP TABLE IF EXISTS `account`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `account` (
  `id` int unsigned NOT NULL AUTO_INCREMENT COMMENT 'Identifier',
  `username` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `salt` binary(32) NOT NULL,
  `verifier` binary(32) NOT NULL,
  `session_key_auth` binary(40) DEFAULT NULL,
  `session_key_bnet` varbinary(64) DEFAULT NULL,
  `totp_secret` varbinary(128) DEFAULT NULL,
  `email` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `reg_mail` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `joindate` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `last_ip` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '127.0.0.1',
  `last_attempt_ip` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '127.0.0.1',
  `failed_logins` int unsigned NOT NULL DEFAULT '0',
  `locked` tinyint unsigned NOT NULL DEFAULT '0',
  `lock_country` varchar(2) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '00',
  `last_login` timestamp NULL DEFAULT NULL,
  `online` tinyint unsigned NOT NULL DEFAULT '0',
  `expansion` tinyint unsigned NOT NULL DEFAULT '0',
  `mutetime` bigint NOT NULL DEFAULT '0',
  `mutereason` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `muteby` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `client_build` int unsigned NOT NULL DEFAULT '0',
  `locale` tinyint unsigned NOT NULL DEFAULT '0',
  `os` varchar(4) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `timezone_offset` smallint NOT NULL DEFAULT '0',
  `recruiter` int unsigned NOT NULL DEFAULT '0',
  `battlenet_account` int unsigned DEFAULT NULL,
  `battlenet_index` tinyint unsigned DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `idx_username` (`username`),
  UNIQUE KEY `uk_bnet_acc` (`battlenet_account`,`battlenet_index`),
  CONSTRAINT `fk_bnet_acc` FOREIGN KEY (`battlenet_account`) REFERENCES `bnot_accounts` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Account System';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `account_access`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `account_access` (
  `AccountID` int unsigned NOT NULL,
  `SecurityLevel` tinyint unsigned NOT NULL,
  `RealmID` int NOT NULL DEFAULT '-1',
  `Comment` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  PRIMARY KEY (`AccountID`,`RealmID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `account_banned`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `account_banned` (
  `id` int unsigned NOT NULL DEFAULT '0' COMMENT 'Account id',
  `bandate` int unsigned NOT NULL DEFAULT '0',
  `unbandate` int unsigned NOT NULL DEFAULT '0',
  `bannedby` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  `banreason` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  `active` tinyint unsigned NOT NULL DEFAULT '1',
  PRIMARY KEY (`id`,`bandate`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Ban List';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `account_boost_token`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `account_boost_token` (
  `ID` bigint unsigned NOT NULL AUTO_INCREMENT,
  `AccountID` int unsigned NOT NULL,
  `BoostType` int unsigned NOT NULL DEFAULT '6',
  `Source` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `UsedRealm` int unsigned NOT NULL DEFAULT '0',
  `UsedGuid` bigint unsigned NOT NULL DEFAULT '0',
  `UsedTime` timestamp NULL DEFAULT NULL,
  `Created` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`ID`),
  KEY `idx_account` (`AccountID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `account_last_played_character`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `account_last_played_character` (
  `accountId` int unsigned NOT NULL,
  `region` tinyint unsigned NOT NULL,
  `battlegroup` tinyint unsigned NOT NULL,
  `realmId` int unsigned DEFAULT NULL,
  `characterName` varchar(12) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `characterGUID` bigint unsigned DEFAULT NULL,
  `lastPlayedTime` int unsigned DEFAULT NULL,
  PRIMARY KEY (`accountId`,`region`,`battlegroup`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `account_login`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `account_login` (
  `account_id` int NOT NULL,
  `srp_version` tinyint NOT NULL DEFAULT '2',
  `salt` varbinary(32) NOT NULL,
  `verifier` varbinary(256) NOT NULL,
  `ticket_hash` binary(32) DEFAULT NULL,
  `ticket_expiry` bigint NOT NULL DEFAULT '0',
  PRIMARY KEY (`account_id`),
  UNIQUE KEY `login_ticket` (`ticket_hash`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `account_muted`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `account_muted` (
  `guid` int unsigned NOT NULL DEFAULT '0' COMMENT 'Global Unique Identifier',
  `mutedate` int unsigned NOT NULL DEFAULT '0',
  `mutetime` int unsigned NOT NULL DEFAULT '0',
  `mutedby` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  `mutereason` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  PRIMARY KEY (`guid`,`mutedate`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='mute List';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `account_shop_points`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `account_shop_points` (
  `AccountID` int unsigned NOT NULL,
  `Points` int unsigned NOT NULL DEFAULT '0',
  `Premium` tinyint unsigned NOT NULL DEFAULT '0',
  `GoldMarketBanned` tinyint unsigned NOT NULL DEFAULT '0',
  `GoldMarketBanReason` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  PRIMARY KEY (`AccountID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `autobroadcast`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `autobroadcast` (
  `realmid` int NOT NULL DEFAULT '-1',
  `id` tinyint unsigned NOT NULL AUTO_INCREMENT,
  `weight` tinyint unsigned DEFAULT '1',
  `text` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  PRIMARY KEY (`id`,`realmid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `battle_pet_declinedname`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `battle_pet_declinedname` (
  `guid` bigint NOT NULL,
  `genitive` varchar(12) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `dative` varchar(12) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `accusative` varchar(12) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `instrumental` varchar(12) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `prepositional` varchar(12) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  PRIMARY KEY (`guid`),
  CONSTRAINT `fk_battle_pet__battle_pet_declinedname` FOREIGN KEY (`guid`) REFERENCES `battle_pets` (`guid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `battle_pet_slots`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `battle_pet_slots` (
  `id` tinyint NOT NULL,
  `battlenetAccountId` int NOT NULL,
  `battlePetGuid` bigint NOT NULL,
  `locked` tinyint NOT NULL DEFAULT '1',
  PRIMARY KEY (`id`,`battlenetAccountId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `battle_pets`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `battle_pets` (
  `guid` bigint NOT NULL,
  `battlenetAccountId` int NOT NULL,
  `species` int NOT NULL,
  `breed` smallint NOT NULL,
  `displayId` int NOT NULL DEFAULT '0',
  `level` smallint NOT NULL DEFAULT '1',
  `exp` smallint NOT NULL DEFAULT '0',
  `health` int NOT NULL DEFAULT '1',
  `quality` tinyint NOT NULL DEFAULT '0',
  `flags` smallint NOT NULL DEFAULT '0',
  `name` varchar(12) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  `nameTimestamp` bigint NOT NULL DEFAULT '0',
  `owner` bigint DEFAULT NULL,
  `ownerRealmId` int DEFAULT NULL,
  PRIMARY KEY (`guid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_account_bans`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_account_bans` (
  `id` int unsigned NOT NULL DEFAULT '0' COMMENT 'Account id',
  `bandate` int unsigned NOT NULL DEFAULT '0',
  `unbandate` int unsigned NOT NULL DEFAULT '0',
  `bannedby` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  `banreason` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  PRIMARY KEY (`id`,`bandate`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Ban List';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_account_heirlooms`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_account_heirlooms` (
  `accountId` int unsigned NOT NULL,
  `itemId` int unsigned NOT NULL DEFAULT '0',
  `flags` int unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`accountId`,`itemId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_account_mounts`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_account_mounts` (
  `battlenetAccountId` int unsigned NOT NULL,
  `mountSpellId` int unsigned NOT NULL,
  `flags` tinyint unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`battlenetAccountId`,`mountSpellId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_account_player_data_element`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_account_player_data_element` (
  `battlenetAccountId` int unsigned NOT NULL,
  `playerDataElementAccountId` int unsigned NOT NULL,
  `floatValue` float DEFAULT NULL,
  `int64Value` bigint DEFAULT NULL,
  PRIMARY KEY (`battlenetAccountId`,`playerDataElementAccountId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_account_player_data_flag`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_account_player_data_flag` (
  `battlenetAccountId` int unsigned NOT NULL,
  `storageIndex` int unsigned NOT NULL,
  `mask` bigint unsigned NOT NULL,
  PRIMARY KEY (`battlenetAccountId`,`storageIndex`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_account_toys`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_account_toys` (
  `accountId` int unsigned NOT NULL,
  `itemId` int NOT NULL DEFAULT '0',
  `isFavourite` tinyint(1) DEFAULT '0',
  `hasFanfare` tinyint(1) DEFAULT '0',
  PRIMARY KEY (`accountId`,`itemId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_account_transmog_illusions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_account_transmog_illusions` (
  `battlenetAccountId` int unsigned NOT NULL,
  `blobIndex` smallint unsigned NOT NULL,
  `illusionMask` int unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`battlenetAccountId`,`blobIndex`),
  CONSTRAINT `bnot_account_transmog_illusions_ibfk_1` FOREIGN KEY (`battlenetAccountId`) REFERENCES `bnot_accounts` (`id`) ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_account_transmog_outfits`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_account_transmog_outfits` (
  `battlenetAccountId` int unsigned NOT NULL,
  `transmogOutfitId` int NOT NULL DEFAULT '0',
  PRIMARY KEY (`battlenetAccountId`,`transmogOutfitId`),
  CONSTRAINT `fk_battlenet_account_transmog_outfits` FOREIGN KEY (`battlenetAccountId`) REFERENCES `bnot_accounts` (`id`) ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_account_warband_scenes`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_account_warband_scenes` (
  `battlenetAccountId` int unsigned NOT NULL,
  `warbandSceneId` int NOT NULL DEFAULT '0',
  `isFavorite` tinyint(1) DEFAULT '0',
  `hasFanfare` tinyint(1) DEFAULT '0',
  PRIMARY KEY (`battlenetAccountId`,`warbandSceneId`),
  CONSTRAINT `fk_battlenet_account_warband_scenes__accountId` FOREIGN KEY (`battlenetAccountId`) REFERENCES `bnot_accounts` (`id`) ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_accounts`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_accounts` (
  `id` int unsigned NOT NULL AUTO_INCREMENT COMMENT 'Identifier',
  `email` varchar(320) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  `battle_tag` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `srp_version` tinyint NOT NULL DEFAULT '1',
  `salt` binary(32) NOT NULL,
  `verifier` blob NOT NULL,
  `joindate` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `last_ip` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '127.0.0.1',
  `failed_logins` int unsigned NOT NULL DEFAULT '0',
  `locked` tinyint unsigned NOT NULL DEFAULT '0',
  `lock_country` varchar(2) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '00',
  `last_login` timestamp NULL DEFAULT NULL,
  `online` tinyint unsigned NOT NULL DEFAULT '0',
  `locale` tinyint unsigned NOT NULL DEFAULT '0',
  `os` varchar(4) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `LastCharacterUndelete` int unsigned NOT NULL DEFAULT '0',
  `LoginTicket` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `LoginTicketExpiry` int unsigned DEFAULT NULL,
  `authenticator_secret` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_battle_tag` (`battle_tag`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Account System';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_blocks`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_blocks` (
  `account_id` int unsigned NOT NULL,
  `blocked_account_id` int unsigned NOT NULL,
  PRIMARY KEY (`account_id`,`blocked_account_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_friend_invitations`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_friend_invitations` (
  `id` int unsigned NOT NULL AUTO_INCREMENT,
  `inviter_account_id` int unsigned NOT NULL,
  `invitee_account_id` int unsigned NOT NULL,
  `created` int unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_pair` (`inviter_account_id`,`invitee_account_id`),
  KEY `idx_invitee` (`invitee_account_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_friends`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_friends` (
  `account_id` int unsigned NOT NULL,
  `friend_account_id` int unsigned NOT NULL,
  `created` int unsigned NOT NULL DEFAULT '0',
  `note` varchar(127) NOT NULL DEFAULT '',
  `favorite` tinyint unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`account_id`,`friend_account_id`),
  KEY `idx_friend` (`friend_account_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_gift`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_gift` (
  `id` int unsigned NOT NULL AUTO_INCREMENT,
  `sender` int unsigned NOT NULL,
  `recipient` int unsigned NOT NULL,
  `product_id` int unsigned NOT NULL,
  `note` varchar(200) NOT NULL DEFAULT '',
  `sent` int unsigned NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_group`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_group` (
  `id` int unsigned NOT NULL AUTO_INCREMENT,
  `name` varchar(32) NOT NULL,
  `owner` int unsigned NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_group_member`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_group_member` (
  `group_id` int unsigned NOT NULL,
  `account` int unsigned NOT NULL,
  PRIMARY KEY (`group_id`,`account`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_group_message`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_group_message` (
  `id` bigint unsigned NOT NULL AUTO_INCREMENT,
  `group_id` int unsigned NOT NULL,
  `sender` int unsigned NOT NULL,
  `body` varchar(500) NOT NULL,
  `sent` int unsigned NOT NULL,
  PRIMARY KEY (`id`),
  KEY `room` (`group_id`,`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_item_appearances`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_item_appearances` (
  `battlenetAccountId` int unsigned NOT NULL,
  `blobIndex` smallint unsigned NOT NULL,
  `appearanceMask` int unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`battlenetAccountId`,`blobIndex`),
  CONSTRAINT `fk_battlenet_item_appearances` FOREIGN KEY (`battlenetAccountId`) REFERENCES `bnot_accounts` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_item_favorite_appearances`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_item_favorite_appearances` (
  `battlenetAccountId` int unsigned NOT NULL,
  `itemModifiedAppearanceId` int unsigned NOT NULL,
  PRIMARY KEY (`battlenetAccountId`,`itemModifiedAppearanceId`),
  CONSTRAINT `fk_battlenet_item_favorite_appearances` FOREIGN KEY (`battlenetAccountId`) REFERENCES `bnot_accounts` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_message`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_message` (
  `id` bigint unsigned NOT NULL AUTO_INCREMENT,
  `sender` int unsigned NOT NULL,
  `recipient` int unsigned NOT NULL,
  `body` varchar(500) NOT NULL,
  `sent` int unsigned NOT NULL,
  `seen` tinyint unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  KEY `inbox` (`recipient`,`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_notice`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_notice` (
  `id` bigint unsigned NOT NULL AUTO_INCREMENT,
  `account` int unsigned NOT NULL,
  `kind` varchar(32) NOT NULL,
  `text` varchar(200) NOT NULL,
  `sent` int unsigned NOT NULL,
  `unread` tinyint unsigned NOT NULL DEFAULT '1',
  PRIMARY KEY (`id`),
  KEY `account` (`account`,`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_social`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_social` (
  `account` int unsigned NOT NULL,
  `status` varchar(16) NOT NULL DEFAULT 'offline',
  `seen` int unsigned NOT NULL DEFAULT '0',
  `game` varchar(64) NOT NULL DEFAULT '',
  `character_name` varchar(16) NOT NULL DEFAULT '',
  `realm_name` varchar(64) NOT NULL DEFAULT '',
  `bio` varchar(200) NOT NULL DEFAULT '',
  `visibility` tinyint unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`account`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bnot_sso_tokens`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bnot_sso_tokens` (
  `token` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  `battlenetAccountId` int unsigned NOT NULL,
  `accountId` int unsigned NOT NULL,
  `realmId` int unsigned NOT NULL,
  `characterGuid` bigint unsigned NOT NULL,
  `issued` bigint NOT NULL,
  `expires` bigint NOT NULL,
  `ip` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  `characterName` varchar(12) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `mapId` int unsigned NOT NULL DEFAULT '0',
  `zoneId` int unsigned NOT NULL DEFAULT '0',
  `posX` float NOT NULL DEFAULT '0',
  `posY` float NOT NULL DEFAULT '0',
  `posZ` float NOT NULL DEFAULT '0',
  PRIMARY KEY (`token`),
  KEY `idx_expires` (`expires`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `build_auth_key`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `build_auth_key` (
  `build` int NOT NULL,
  `platform` char(4) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `arch` char(4) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `type` char(4) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `key` binary(16) NOT NULL,
  PRIMARY KEY (`build`,`platform`,`arch`,`type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `build_executable_hash`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `build_executable_hash` (
  `build` int NOT NULL,
  `platform` char(4) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `executableHash` binary(20) NOT NULL,
  PRIMARY KEY (`build`,`platform`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `build_info`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `build_info` (
  `build` int NOT NULL,
  `majorVersion` int DEFAULT NULL,
  `minorVersion` int DEFAULT NULL,
  `bugfixVersion` int DEFAULT NULL,
  `hotfixVersion` char(3) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  PRIMARY KEY (`build`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `ip_banned`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `ip_banned` (
  `ip` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '127.0.0.1',
  `bandate` int unsigned NOT NULL,
  `unbandate` int unsigned NOT NULL,
  `bannedby` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '[Console]',
  `banreason` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'no reason',
  PRIMARY KEY (`ip`,`bandate`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Banned IPs';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `logs`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `logs` (
  `time` int unsigned NOT NULL,
  `realm` int unsigned NOT NULL,
  `type` varchar(250) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  `level` tinyint unsigned NOT NULL DEFAULT '0',
  `string` mediumtext CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `logs_ip_actions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `logs_ip_actions` (
  `id` int unsigned NOT NULL AUTO_INCREMENT COMMENT 'Unique Identifier',
  `account_id` int unsigned NOT NULL COMMENT 'Account ID',
  `character_guid` bigint unsigned NOT NULL COMMENT 'Character Guid',
  `realm_id` int unsigned NOT NULL DEFAULT '0' COMMENT 'Realm ID',
  `type` tinyint unsigned NOT NULL,
  `ip` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '127.0.0.1',
  `systemnote` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci COMMENT 'Notes inserted by system',
  `unixtime` int unsigned NOT NULL COMMENT 'Unixtime',
  `time` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Timestamp',
  `comment` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci COMMENT 'Allows users to add a comment',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Used to log ips of individual actions';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `rbac_account_permissions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `rbac_account_permissions` (
  `accountId` int unsigned NOT NULL COMMENT 'Account id',
  `permissionId` int unsigned NOT NULL COMMENT 'Permission id',
  `granted` tinyint(1) NOT NULL DEFAULT '1' COMMENT 'Granted = 1, Denied = 0',
  `realmId` int NOT NULL DEFAULT '-1' COMMENT 'Realm Id, -1 means all',
  PRIMARY KEY (`accountId`,`permissionId`,`realmId`),
  KEY `fk__rbac_account_roles__rbac_permissions` (`permissionId`),
  CONSTRAINT `fk__rbac_account_permissions__account` FOREIGN KEY (`accountId`) REFERENCES `account` (`id`) ON DELETE CASCADE,
  CONSTRAINT `fk__rbac_account_roles__rbac_permissions` FOREIGN KEY (`permissionId`) REFERENCES `rbac_permissions` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Account-Permission relation';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `rbac_default_permissions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `rbac_default_permissions` (
  `secId` int unsigned NOT NULL COMMENT 'Security Level id',
  `permissionId` int unsigned NOT NULL COMMENT 'permission id',
  `realmId` int NOT NULL DEFAULT '-1' COMMENT 'Realm Id, -1 means all',
  PRIMARY KEY (`secId`,`permissionId`,`realmId`),
  KEY `fk__rbac_default_permissions__rbac_permissions` (`permissionId`),
  CONSTRAINT `fk__rbac_default_permissions__rbac_permissions` FOREIGN KEY (`permissionId`) REFERENCES `rbac_permissions` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Default permission to assign to different account security levels';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `rbac_linked_permissions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `rbac_linked_permissions` (
  `id` int unsigned NOT NULL COMMENT 'Permission id',
  `linkedId` int unsigned NOT NULL COMMENT 'Linked Permission id',
  PRIMARY KEY (`id`,`linkedId`),
  KEY `fk__rbac_linked_permissions__rbac_permissions1` (`id`),
  KEY `fk__rbac_linked_permissions__rbac_permissions2` (`linkedId`),
  CONSTRAINT `fk__rbac_linked_permissions__rbac_permissions1` FOREIGN KEY (`id`) REFERENCES `rbac_permissions` (`id`) ON DELETE CASCADE,
  CONSTRAINT `fk__rbac_linked_permissions__rbac_permissions2` FOREIGN KEY (`linkedId`) REFERENCES `rbac_permissions` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Permission - Linked Permission relation';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `rbac_permissions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `rbac_permissions` (
  `id` int unsigned NOT NULL DEFAULT '0' COMMENT 'Permission id',
  `name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL COMMENT 'Permission name',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Permission List';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `realmcharacters`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `realmcharacters` (
  `realmid` int unsigned NOT NULL DEFAULT '0',
  `acctid` int unsigned NOT NULL,
  `numchars` tinyint unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`realmid`,`acctid`),
  KEY `acctid` (`acctid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Realm Character Tracker';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `realmlist`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `realmlist` (
  `id` int unsigned NOT NULL AUTO_INCREMENT,
  `name` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `address` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '127.0.0.1',
  `localAddress` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '127.0.0.1',
  `address3` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `address4` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `localSubnetMask` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '255.255.255.0',
  `port` smallint unsigned NOT NULL DEFAULT '8085',
  `icon` tinyint unsigned NOT NULL DEFAULT '0',
  `flag` tinyint unsigned NOT NULL DEFAULT '2',
  `timezone` tinyint unsigned NOT NULL DEFAULT '0',
  `allowedSecurityLevel` tinyint unsigned NOT NULL DEFAULT '0',
  `population` float NOT NULL DEFAULT '0',
  `gamebuild` int unsigned NOT NULL DEFAULT '70291',
  `Region` tinyint unsigned NOT NULL DEFAULT '1',
  `Battlegroup` tinyint unsigned NOT NULL DEFAULT '1',
  `contentSetId` int unsigned NOT NULL DEFAULT '137',
  `hardcore` tinyint unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  UNIQUE KEY `idx_name` (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Realm System';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `secret_digest`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `secret_digest` (
  `id` int unsigned NOT NULL,
  `digest` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `shop_config`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `shop_config` (
  `Name` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  `Value` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  PRIMARY KEY (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `shop_purchase_log`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `shop_purchase_log` (
  `ID` bigint unsigned NOT NULL AUTO_INCREMENT,
  `AccountID` int unsigned NOT NULL,
  `ProductID` int unsigned NOT NULL,
  `Price` int unsigned NOT NULL,
  `CharacterGUID` bigint unsigned NOT NULL,
  `Time` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`ID`),
  KEY `idx_account` (`AccountID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `support_ticket_replies`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `support_ticket_replies` (
  `id` int unsigned NOT NULL AUTO_INCREMENT,
  `ticketId` int unsigned NOT NULL,
  `accountId` int unsigned NOT NULL,
  `isStaff` tinyint unsigned NOT NULL DEFAULT '0',
  `message` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  `created` bigint NOT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_ticket` (`ticketId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `support_tickets`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `support_tickets` (
  `id` int unsigned NOT NULL AUTO_INCREMENT,
  `battlenetAccountId` int unsigned NOT NULL,
  `accountId` int unsigned NOT NULL,
  `realmId` int unsigned NOT NULL,
  `characterGuid` bigint unsigned NOT NULL DEFAULT '0',
  `characterName` varchar(12) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `category` enum('bug','stuck','account','player','other') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  `subject` varchar(120) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  `message` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  `mapId` int unsigned NOT NULL DEFAULT '0',
  `zoneId` int unsigned NOT NULL DEFAULT '0',
  `posX` float NOT NULL DEFAULT '0',
  `posY` float NOT NULL DEFAULT '0',
  `posZ` float NOT NULL DEFAULT '0',
  `status` enum('open','answered','closed') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'open',
  `created` bigint NOT NULL,
  `updated` bigint NOT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_account` (`battlenetAccountId`),
  KEY `idx_status` (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `support_unstuck_log`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `support_unstuck_log` (
  `id` int unsigned NOT NULL AUTO_INCREMENT,
  `accountId` int unsigned NOT NULL,
  `realmId` int unsigned NOT NULL,
  `characterGuid` bigint unsigned NOT NULL,
  `destination` enum('inn','graveyard') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
  `result` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `time` bigint NOT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_character` (`realmId`,`characterGuid`,`time`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `uptime`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `uptime` (
  `realmid` int unsigned NOT NULL,
  `starttime` int unsigned NOT NULL DEFAULT '0',
  `uptime` int unsigned NOT NULL DEFAULT '0',
  `maxplayers` smallint unsigned NOT NULL DEFAULT '0',
  `revision` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'Trinitycore',
  PRIMARY KEY (`realmid`,`starttime`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Uptime system';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `vw_log_history`;
/*!50001 DROP VIEW IF EXISTS `vw_log_history`*/;
SET @saved_cs_client     = @@character_set_client;
/*!50503 SET character_set_client = utf8mb4 */;
/*!50001 CREATE VIEW `vw_log_history` AS SELECT 
 1 AS `First Logged`,
 1 AS `Last Logged`,
 1 AS `Occurrences`,
 1 AS `Realm`,
 1 AS `type`,
 1 AS `level`,
 1 AS `string`*/;
SET character_set_client = @saved_cs_client;
DROP TABLE IF EXISTS `vw_rbac`;
/*!50001 DROP VIEW IF EXISTS `vw_rbac`*/;
SET @saved_cs_client     = @@character_set_client;
/*!50503 SET character_set_client = utf8mb4 */;
/*!50001 CREATE VIEW `vw_rbac` AS SELECT 
 1 AS `Permission ID`,
 1 AS `Permission Group`,
 1 AS `Security Level`,
 1 AS `Permission`*/;
SET character_set_client = @saved_cs_client;
/*!50001 DROP VIEW IF EXISTS `vw_log_history`*/;
/*!50001 SET @saved_cs_client          = @@character_set_client */;
/*!50001 SET @saved_cs_results         = @@character_set_results */;
/*!50001 SET @saved_col_connection     = @@collation_connection */;
/*!50001 SET character_set_client      = utf8mb3 */;
/*!50001 SET character_set_results     = utf8mb3 */;
/*!50001 SET collation_connection      = utf8mb3_general_ci */;
/*!50001 CREATE ALGORITHM=UNDEFINED */

/*!50001 VIEW `vw_log_history` AS select from_unixtime(min(`logs`.`time`)) AS `First Logged`,from_unixtime(max(`logs`.`time`)) AS `Last Logged`,count(0) AS `Occurrences`,`realmlist`.`name` AS `Realm`,`logs`.`type` AS `type`,`logs`.`level` AS `level`,`logs`.`string` AS `string` from (`logs` left join `realmlist` on((`logs`.`realm` = `realmlist`.`id`))) group by `logs`.`string`,`logs`.`type`,`logs`.`realm` */;
/*!50001 SET character_set_client      = @saved_cs_client */;
/*!50001 SET character_set_results     = @saved_cs_results */;
/*!50001 SET collation_connection      = @saved_col_connection */;
/*!50001 DROP VIEW IF EXISTS `vw_rbac`*/;
/*!50001 SET @saved_cs_client          = @@character_set_client */;
/*!50001 SET @saved_cs_results         = @@character_set_results */;
/*!50001 SET @saved_col_connection     = @@collation_connection */;
/*!50001 SET character_set_client      = utf8mb3 */;
/*!50001 SET character_set_results     = utf8mb3 */;
/*!50001 SET collation_connection      = utf8mb3_general_ci */;
/*!50001 CREATE ALGORITHM=UNDEFINED */

/*!50001 VIEW `vw_rbac` AS select `t1`.`linkedId` AS `Permission ID`,`t1`.`id` AS `Permission Group`,ifnull(`t2`.`secId`,'linked') AS `Security Level`,`t3`.`name` AS `Permission` from ((`rbac_linked_permissions` `t1` left join `rbac_default_permissions` `t2` on((`t1`.`id` = `t2`.`permissionId`))) left join `rbac_permissions` `t3` on((`t1`.`linkedId` = `t3`.`id`))) */;
/*!50001 SET character_set_client      = @saved_cs_client */;
/*!50001 SET character_set_results     = @saved_cs_results */;
/*!50001 SET collation_connection      = @saved_col_connection */;
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;


-- Seed data

/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;

/*!40000 ALTER TABLE `build_auth_key` DISABLE KEYS */;
INSERT INTO `build_auth_key` VALUES (25549,'Mac','x64','WoW',_binary 'fü^	¸pa&y_\ÈÁ\Ø');
INSERT INTO `build_auth_key` VALUES (25549,'Win','x64','WoW',_binary 'RbN\Ø\Ë\Öú\Ç\Ó?]g¥5\ó');
INSERT INTO `build_auth_key` VALUES (25549,'Win','x86','WoW',_binary 'şYO\Ã^šÿ†ÙŠ6J²—');
INSERT INTO `build_auth_key` VALUES (25996,'Mac','x64','WoW',_binary '!—I\Ö\õl¬›­\òª\É');
INSERT INTO `build_auth_key` VALUES (25996,'Win','x64','WoW',_binary '\Çÿ“-j!t£\Õ8\Êrm+');
INSERT INTO `build_auth_key` VALUES (25996,'Win','x86','WoW',_binary '#ÅœYc\Ë\ï[r¥x\ß\Ë');
INSERT INTO `build_auth_key` VALUES (26124,'Mac','x64','WoW',_binary '\ÉÊ™z¸\í\á\ÆTe\Ë) †œN');
INSERT INTO `build_auth_key` VALUES (26124,'Win','x64','WoW',_binary 'F\ß\Ğ{¦{¤š\õSC^	?');
INSERT INTO `build_auth_key` VALUES (26124,'Win','x86','WoW',_binary 'øÀZ\ãr\Ş\Êl\Úz\\9');
INSERT INTO `build_auth_key` VALUES (26365,'Mac','x64','WoW',_binary '\Û\çø`\'mk@\nª†³]Q¤');
INSERT INTO `build_auth_key` VALUES (26365,'Win','x64','WoW',_binary 'Y¥?0rˆEKA›\æ”\ßP<');
INSERT INTO `build_auth_key` VALUES (26365,'Win','x86','WoW',_binary '*¬‚\È‚,©\×ú\Zƒ:');
INSERT INTO `build_auth_key` VALUES (26654,'Mac','x64','WoW',_binary '’4Á½^–‡­½\÷d\ò\à\è');
INSERT INTO `build_auth_key` VALUES (26654,'Win','x64','WoW',_binary '§Rd‹™ş[WÁ2Ä’‰Z');
INSERT INTO `build_auth_key` VALUES (26654,'Win','x86','WoW',_binary 'ú\ÂÖ“\ç¹\ìŸu$V–\Ø');
INSERT INTO `build_auth_key` VALUES (26822,'Mac','x64','WoW',_binary '‘\06h\ÂE\ÑNÍ\ğ”\àe\àk');
INSERT INTO `build_auth_key` VALUES (26822,'Win','x64','WoW',_binary '+\ö\×FÀ\Æ\Ì~\÷”P³	\å•');
INSERT INTO `build_auth_key` VALUES (26822,'Win','x86','WoW',_binary '(>w\ì\÷\æ4{\ä\ë™\Ç\Ç');
INSERT INTO `build_auth_key` VALUES (26899,'Mac','x64','WoW',_binary 'ƒh\ï\Â)\n3)‚\0\Ô');
INSERT INTO `build_auth_key` VALUES (26899,'Win','x64','WoW',_binary '5Q\ï\0(µ’Y½%dK');
INSERT INTO `build_auth_key` VALUES (26899,'Win','x86','WoW',_binary '\ôb\Í/\ä\ê>­øu0Û±Œ™');
INSERT INTO `build_auth_key` VALUES (26972,'Mac','x64','WoW',_binary '4şş=r¬©¤@}\Å5\Ş\Öj');
INSERT INTO `build_auth_key` VALUES (26972,'Win','x64','WoW',_binary 'n!-\ïj$£Ù­\õ\ã\"\÷®');
INSERT INTO `build_auth_key` VALUES (26972,'Win','x86','WoW',_binary 'y~\Ìf-\Ë\Õ	\nD?&');
INSERT INTO `build_auth_key` VALUES (28153,'Win','x64','WoW',_binary '\İbe\Ìm1“+G™4\Ì\Ü\n¿');
INSERT INTO `build_auth_key` VALUES (30706,'Win','x64','WoW',_binary '»m˜fşJ¥hQ˜x0ü');
INSERT INTO `build_auth_key` VALUES (30993,'Win','x64','WoW',_binary '+­aeZ¼/\Ã\ĞH“µ6@:‘');
INSERT INTO `build_auth_key` VALUES (31229,'Win','x64','WoW',_binary 'ŠF\ò6p0Ÿ*®…É¤rv8+');
INSERT INTO `build_auth_key` VALUES (31429,'Win','x64','WoW',_binary 'w•¥¯\ÃR^ÿrO\î\ç');
INSERT INTO `build_auth_key` VALUES (31478,'Win','x64','WoW',_binary 'ys¨\ÕKÛ‹y’—°–\çq\ï');
INSERT INTO `build_auth_key` VALUES (32305,'Win','x64','WoW',_binary '!\õ¦üzØŸ¿AÚ‹‡8j');
INSERT INTO `build_auth_key` VALUES (32494,'Win','x64','WoW',_binary 'X˜J\Î‘”ƒ\\a0š„Š');
INSERT INTO `build_auth_key` VALUES (32580,'Win','x64','WoW',_binary '‡\Âú ×“\ğ)%À\İ\Ê');
INSERT INTO `build_auth_key` VALUES (32638,'Win','x64','WoW',_binary ']\ì\çÔ¨g\İ\Şa]­\"·mN');
INSERT INTO `build_auth_key` VALUES (32722,'Win','x64','WoW',_binary '\Z	¾8¡\"XkI1¾\Ì\êÔª');
INSERT INTO `build_auth_key` VALUES (32750,'Mac','x64','WoW',_binary '\ïNM	¢¨\ÔÀŞ¼p†');
INSERT INTO `build_auth_key` VALUES (32750,'Win','x64','WoW',_binary '\Å\ËfŸZ[#}UCw2');
INSERT INTO `build_auth_key` VALUES (32978,'Mac','x64','WoW',_binary 'RÁøG\ç•\Ö\ëE\'Œ\Ô3\ó9');
INSERT INTO `build_auth_key` VALUES (32978,'Win','x64','WoW',_binary 'v®. >R]—\õhˆC\õH\0');
INSERT INTO `build_auth_key` VALUES (33369,'Mac','x64','WoW',_binary '\õ¨I\Ç\nT\ğ~£«ƒ>¿fq');
INSERT INTO `build_auth_key` VALUES (33369,'Win','x64','WoW',_binary 'Y†¬°M<@?V ÏŒO\n');
INSERT INTO `build_auth_key` VALUES (33528,'Win','x64','WoW',_binary '\Î<©±’\÷\Òy,x[G\ß');
INSERT INTO `build_auth_key` VALUES (33724,'Win','x64','WoW',_binary '8\÷»\Ï(I9\İ \è\ÆL\Ûùşw');
INSERT INTO `build_auth_key` VALUES (33775,'Mac','x64','WoW',_binary '5M-\æ\Ñ$\î˜\÷k6üü');
INSERT INTO `build_auth_key` VALUES (33775,'Win','x64','WoW',_binary '¸&0\n„I\ín\ñn§Gú-.');
INSERT INTO `build_auth_key` VALUES (33941,'Win','x64','WoW',_binary 'ˆ¯\Z6\Òw\r\nl †Ip–¨‰');
INSERT INTO `build_auth_key` VALUES (34220,'Win','x64','WoW',_binary 'µ\ã[—lk¯‚PW\0\ç\Ùfj,');
INSERT INTO `build_auth_key` VALUES (34601,'Win','x64','WoW',_binary '\r}\ór_«¤\ğ	%w™¡c');
INSERT INTO `build_auth_key` VALUES (34769,'Win','x64','WoW',_binary '“ù¹¯c—\ã\ä\î\ÙM6\Ñi\Ò');
INSERT INTO `build_auth_key` VALUES (34963,'Mac','x64','WoW',_binary '\ÅeŠ\ç4Gºª\äm\n');
INSERT INTO `build_auth_key` VALUES (34963,'Win','x64','WoW',_binary '{¥‡œ]\"#°*\Ã`:');
INSERT INTO `build_auth_key` VALUES (35249,'Win','x64','WoW',_binary 'Ç±š\éÿ	\õX)³\Ñ\r');
INSERT INTO `build_auth_key` VALUES (35284,'Mac','x64','WoW',_binary '¦ \nÅ§=«/\ÜÇ›²R¯');
INSERT INTO `build_auth_key` VALUES (35284,'Win','x64','WoW',_binary '\ê8\ç\Üı 	\Ûüƒ\î<O');
INSERT INTO `build_auth_key` VALUES (35435,'Mac','x64','WoW',_binary '\æWÁJF¼\Û,\æ\Ú7\ä0E');
INSERT INTO `build_auth_key` VALUES (35435,'Win','x64','WoW',_binary '»9z’ş#t¥/Âµº.\È\à');
INSERT INTO `build_auth_key` VALUES (35662,'Mac','x64','WoW',_binary 'Yfl6\Ù\÷ª¶\îg');
INSERT INTO `build_auth_key` VALUES (35662,'Win','x64','WoW',_binary 'W‹\ÉHp\Âx\Ëib\óm\Â»');
INSERT INTO `build_auth_key` VALUES (36753,'Win','x64','WoW',_binary '8oŞ…Yµ\ê\Ö{yI ˆ');
INSERT INTO `build_auth_key` VALUES (36839,'Win','x64','WoW',_binary '5n´A+ü\÷.:\õ\rQ\Õ)');
INSERT INTO `build_auth_key` VALUES (36949,'Win','x64','WoW',_binary 'QÀtÍŠ	§S„¹´Aˆ\Åi');
INSERT INTO `build_auth_key` VALUES (37142,'Win','x64','WoW',_binary ']œû19\ğÑ¶Â³&«\É');
INSERT INTO `build_auth_key` VALUES (37176,'Win','x64','WoW',_binary '<r^¥\ì=®\ÑC\ëo\ó´Œ\Ú');
INSERT INTO `build_auth_key` VALUES (37474,'Mac','x64','WoW',_binary 'L›\ç\äB7·\èmB\æx\Ô3');
INSERT INTO `build_auth_key` VALUES (37474,'Win','x64','WoW',_binary '\ræ…»°U†\çû\ÜK°j[');
INSERT INTO `build_auth_key` VALUES (38134,'Win','x64','WoW',_binary '2\'^\Ğ\ñ;5|(½°\æ\ï1');
INSERT INTO `build_auth_key` VALUES (38556,'Win','x64','WoW',_binary '\ì}Z\öCd¬>q\óû¡³¨‚');
INSERT INTO `build_auth_key` VALUES (39653,'Win','x64','WoW',_binary '\Ğ«\ë‘1(\Ù\Å~\âF2');
INSERT INTO `build_auth_key` VALUES (39804,'Win','x64','WoW',_binary '\ä-+º\í&\nvù±\äwá¥');
INSERT INTO `build_auth_key` VALUES (40000,'Win','x64','WoW',_binary 'L±C:¶7\ğŸû½\"°');
INSERT INTO `build_auth_key` VALUES (40120,'Mac','x64','WoW',_binary '…?)…Î®\Ôm\ô\"X<\Ğz|');
INSERT INTO `build_auth_key` VALUES (40120,'Win','x64','WoW',_binary '\ô~®ı‹\ŞşªP\ê3fx');
INSERT INTO `build_auth_key` VALUES (40443,'Win','x64','WoW',_binary '…—»C\è«8\ÈUè¿·*»\õ');
INSERT INTO `build_auth_key` VALUES (40593,'Win','x64','WoW',_binary 'ºW-b\Õ\öS9Ad¨\Ú\â');
INSERT INTO `build_auth_key` VALUES (40725,'Win','x64','WoW',_binary 'Á\ë\Ûë›²•n¼\Î\÷\É\Òz;');
INSERT INTO `build_auth_key` VALUES (40906,'Win','x64','WoW',_binary '\õü%œ†5HŠş\r\Ğ#\óa\Ô');
INSERT INTO `build_auth_key` VALUES (40944,'Win','x64','WoW',_binary '6\Çúºô‡¨ IÁpept');
INSERT INTO `build_auth_key` VALUES (40966,'Win','x64','WoW',_binary '\ÙG¯!\ó\Ò\Ø\óv;™K¬ˆ');
INSERT INTO `build_auth_key` VALUES (41031,'Win','x64','WoW',_binary 'š¬Ö°\Ö7K{¦š[gtI');
INSERT INTO `build_auth_key` VALUES (41079,'Win','x64','WoW',_binary 'ø…<ø#¼¾Š–w§bß®\á');
INSERT INTO `build_auth_key` VALUES (41288,'Win','x64','WoW',_binary '‡–‘\Û\Å6\ë$¶\Ç?­[');
INSERT INTO `build_auth_key` VALUES (41323,'Win','x64','WoW',_binary '\å=\r\ñúÁ¥š€q²• J');
INSERT INTO `build_auth_key` VALUES (41359,'Win','x64','WoW',_binary '_*i\nCu¡µ*(\ÖÖú');
INSERT INTO `build_auth_key` VALUES (41488,'Win','x64','WoW',_binary '\É\ÃhpX\ó\õ2µS\ÚÙ');
INSERT INTO `build_auth_key` VALUES (41793,'Win','x64','WoW',_binary '³´}£·aUpt*U¹f\î');
INSERT INTO `build_auth_key` VALUES (42010,'Win','x64','WoW',_binary '0)pA{[\åS\ÌS\Z');
INSERT INTO `build_auth_key` VALUES (42423,'Win','x64','WoW',_binary 'aJ~”\İWT…–¾B\Â');
INSERT INTO `build_auth_key` VALUES (42488,'Win','x64','WoW',_binary '§‡Uæ’ƒ¢q\Å\Ñ\î<\Ûo');
INSERT INTO `build_auth_key` VALUES (42521,'Win','x64','WoW',_binary '_\æÁ/\ÄÆ±[J];[J];');
INSERT INTO `build_auth_key` VALUES (42538,'Win','x64','WoW',_binary 'q§PK\Õ?\å\òBe\Ós®');
INSERT INTO `build_auth_key` VALUES (42560,'Win','x64','WoW',_binary '_\èÃŠg\ÊFd».\rş');
INSERT INTO `build_auth_key` VALUES (42614,'Win','x64','WoW',_binary 'w+\ç&ş\ïBBU\Ò\êys\Ê');
INSERT INTO `build_auth_key` VALUES (42698,'Win','x64','WoW',_binary '´I{\Ñ\Ét\Åû	TŠ\Âri');
INSERT INTO `build_auth_key` VALUES (42825,'Win','x64','WoW',_binary '¡M¢(Æ¦¯\ñİºQ!‰9\åW');
INSERT INTO `build_auth_key` VALUES (42852,'Win','x64','WoW',_binary 'ŞŸŸ<\ÈıTÓ¯ùœ\ïü\á)');
INSERT INTO `build_auth_key` VALUES (42937,'Win','x64','WoW',_binary '\õüu\çtu,’„k33’c');
INSERT INTO `build_auth_key` VALUES (42979,'Win','x64','WoW',_binary '\á\İ8®dPüM*\ä`’3ÅT');
INSERT INTO `build_auth_key` VALUES (43114,'Win','x64','WoW',_binary '\÷\\“€Ì²JH¢N\îRÁYJ~');
INSERT INTO `build_auth_key` VALUES (43206,'Win','x64','WoW',_binary '\İ\èS,wÿ·_%m\Å\ñ\ó\Ù');
INSERT INTO `build_auth_key` VALUES (43340,'Win','x64','WoW',_binary 'p\äm-ˆ„ß“ê„›Œ\ô');
INSERT INTO `build_auth_key` VALUES (43345,'Win','x64','WoW',_binary '\Ù«ü\Ú\rîŒ¯N\ã\ö\r\î');
INSERT INTO `build_auth_key` VALUES (43971,'Win','x64','WoW',_binary 'hùaû\0¥\Ç\Ó\n­\Ù');
INSERT INTO `build_auth_key` VALUES (44015,'Win','x64','WoW',_binary 'ü\ğ½§É‹şù*\æ\ØÃš!z½');
INSERT INTO `build_auth_key` VALUES (44061,'Win','x64','WoW',_binary 'ı+\\2“şœªn°·x');
INSERT INTO `build_auth_key` VALUES (44127,'Win','x64','WoW',_binary 'xx‡\Î\ÉüÉµH`\äü4¨');
INSERT INTO `build_auth_key` VALUES (44232,'Win','x64','WoW',_binary '\ğ§\÷\é‡;³u\0\"\ÖM3\Ï');
INSERT INTO `build_auth_key` VALUES (44325,'Win','x64','WoW',_binary 'Š}RM&Š™4\Ã\ÑH\è\ğ');
INSERT INTO `build_auth_key` VALUES (44730,'Win','x64','WoW',_binary 'ü\Ä{´ÇŸC\0\Ê\ó\å\Ê\Ç');
INSERT INTO `build_auth_key` VALUES (44908,'Win','x64','WoW',_binary '¿ú\ì@É¼Õ‘\Ç\ÉY©Õ¨ºŒ');
INSERT INTO `build_auth_key` VALUES (45114,'Win','x64','WoW',_binary '×¯\â@½\0\ğl0\Ğ\Â\ÑnT¨¾');
INSERT INTO `build_auth_key` VALUES (45161,'Win','x64','WoW',_binary 't½.xz˜±E°c½©©l½');
INSERT INTO `build_auth_key` VALUES (45338,'Win','x64','WoW',_binary '\\\â	JA¶Ú\õcx¼;\à');
INSERT INTO `build_auth_key` VALUES (45745,'Win','x64','WoW',_binary 'm\ÉaiMvZYZ:\ök');
INSERT INTO `build_auth_key` VALUES (46479,'Win','x64','WoW',_binary 'Ëš\ôØ›`£«¢ˆÓ•\Ó\Ù2');
INSERT INTO `build_auth_key` VALUES (46658,'Win','x64','WoW',_binary '?ûT(\×S`\é\ï\â\\\Øcš');
INSERT INTO `build_auth_key` VALUES (46689,'Win','x64','WoW',_binary 'Ù¡j\Ö	\õFu\ïÓª');
INSERT INTO `build_auth_key` VALUES (46702,'Win','x64','WoW',_binary '´\Ñhù}Éªü\Í\n„,');
INSERT INTO `build_auth_key` VALUES (46741,'Win','x64','WoW',_binary 'LJ~\Â	Š\ñû§E„Çšx');
INSERT INTO `build_auth_key` VALUES (46801,'Win','x64','WoW',_binary '\æ¬\Ñ\ê]6«ÿ®^\Ş\Ø0\ß');
INSERT INTO `build_auth_key` VALUES (46879,'Win','x64','WoW',_binary '\ï\ìC“`Q\İ\Z!3¯kc\Û');
INSERT INTO `build_auth_key` VALUES (46924,'Win','x64','WoW',_binary '\æ\Î\Z\ği\ì\ñ\çÛª{²ø');
INSERT INTO `build_auth_key` VALUES (47067,'Win','x64','WoW',_binary 'c†,üŞ¦½+\×\÷@\ë6¶VW');
INSERT INTO `build_auth_key` VALUES (47187,'Win','x64','WoW',_binary 'q„U\Å\0#r’\á\æ\é1\á');
INSERT INTO `build_auth_key` VALUES (47213,'Win','x64','WoW',_binary '#\Å\rˆÎ¬\n†–­Ş\ÒDÔ¢');
INSERT INTO `build_auth_key` VALUES (47631,'Win','x64','WoW',_binary 'ù†«‘Ğ®²\"\ï·/BVq<');
INSERT INTO `build_auth_key` VALUES (47777,'Win','x64','WoW',_binary '¨Œ‘Z¹\à5¡\å\\M\Ï_Ÿ');
INSERT INTO `build_auth_key` VALUES (47799,'Win','x64','WoW',_binary 'sd\ë	<#\Û,Ü•Õ§´“>');
INSERT INTO `build_auth_key` VALUES (47825,'Win','x64','WoW',_binary '‚£¹N^rz\ó¢´qÿ TÀ');
INSERT INTO `build_auth_key` VALUES (47849,'Win','x64','WoW',_binary 'İ‹¾ ‡¢Œ\n\ô˜L¾#¡\Ç');
INSERT INTO `build_auth_key` VALUES (47871,'Win','x64','WoW',_binary 'O}0\îI‚°+;?ˆ7\Â\Ä\ò');
INSERT INTO `build_auth_key` VALUES (47884,'Win','x64','WoW',_binary '+z\0+\ÃY\ò\Ã¼-\àC¿');
INSERT INTO `build_auth_key` VALUES (47936,'Win','x64','WoW',_binary 'ƒ=0\Øû\Ä;?®™\Í8˜\×I');
INSERT INTO `build_auth_key` VALUES (47967,'Win','x64','WoW',_binary '\Ï\â%\Ğ\"MuAÓµ\Âdx');
INSERT INTO `build_auth_key` VALUES (48001,'Win','x64','WoW',_binary 'K`£{\Ù[a^q„i\æÕ»');
INSERT INTO `build_auth_key` VALUES (48069,'Win','x64','WoW',_binary 'UŒß• ‚\éXIwœ|iE\å');
INSERT INTO `build_auth_key` VALUES (48317,'Win','x64','WoW',_binary 'À–\ã{E´2D\éÇ™`MÔ¯');
INSERT INTO `build_auth_key` VALUES (48397,'Win','x64','WoW',_binary 'dº‡y\ê©~lW˜+k\Z[2\ç');
INSERT INTO `build_auth_key` VALUES (48526,'Win','x64','WoW',_binary 'Õ·\Ó0:*ti\î\Z\ë\Ëe');
INSERT INTO `build_auth_key` VALUES (48676,'Win','x64','WoW',_binary '\àYût\ß\öCŒ\Â(d\Ê');
INSERT INTO `build_auth_key` VALUES (48749,'Win','x64','WoW',_binary '’\ÛÌ \ã=ûŠ¢¶£’F²ˆ');
INSERT INTO `build_auth_key` VALUES (48838,'Win','x64','WoW',_binary 'oNF\ï\"-\é{ÄŠª–');
INSERT INTO `build_auth_key` VALUES (48865,'Win','x64','WoW',_binary 'KwJ¾{4\Öp%q´\'šKj');
INSERT INTO `build_auth_key` VALUES (48892,'Win','x64','WoW',_binary 'ª1¿\'Eƒ!°:\Z4id\İ{');
INSERT INTO `build_auth_key` VALUES (48966,'Win','x64','WoW',_binary '‚1B\Ê»q_\õ]CC\å\\m');
INSERT INTO `build_auth_key` VALUES (48999,'Win','x64','WoW',_binary 'yºo\ğùg.\ï‡_d\\‹b\Ô');
INSERT INTO `build_auth_key` VALUES (49267,'Win','x64','WoW',_binary '\î\ç~¥¢\às\ZÛ´\Z\ï±\ß1');
INSERT INTO `build_auth_key` VALUES (49318,'Win','x64','WoW',_binary '¯Cš\îb\îH³l%=›¿');
INSERT INTO `build_auth_key` VALUES (49343,'Win','x64','WoW',_binary '0\ZL	B¹¶\ö¹­l`');
INSERT INTO `build_auth_key` VALUES (49407,'Win','x64','WoW',_binary 'd‚\rÉˆ[°i;7	»/0');
INSERT INTO `build_auth_key` VALUES (49426,'Win','x64','WoW',_binary '\Ø^ß¿\é©JU\â´Q\ä²');
INSERT INTO `build_auth_key` VALUES (49444,'Win','x64','WoW',_binary '6;+([İˆWA(f1m<');
INSERT INTO `build_auth_key` VALUES (49474,'Win','x64','WoW',_binary 'D§Ò³R\î=	Š<´\Â\ñ^7');
INSERT INTO `build_auth_key` VALUES (49570,'Win','x64','WoW',_binary '°$\Şg¬®¹\è\îm³\Å>‚');
INSERT INTO `build_auth_key` VALUES (49679,'Win','x64','WoW',_binary 'œ\å›h\Øy~¿\0XAC');
INSERT INTO `build_auth_key` VALUES (49741,'Win','x64','WoW',_binary '\ñ\â»”l\ó·B*Ş¶\Í\Z');
INSERT INTO `build_auth_key` VALUES (49801,'Win','x64','WoW',_binary '2•g¶l¨]½Vx¶Æƒ');
INSERT INTO `build_auth_key` VALUES (49890,'Win','x64','WoW',_binary '\"¥¸¡\ëyzd™_p[=¼±L');
INSERT INTO `build_auth_key` VALUES (50000,'Win','x64','WoW',_binary '\ğoú\"–ıf8B•\ÛıZL‘');
INSERT INTO `build_auth_key` VALUES (50401,'Win','x64','WoW',_binary '>\ïR\Ù\Ì\è\Ğ\âU\ğªI8');
INSERT INTO `build_auth_key` VALUES (50438,'Win','x64','WoW',_binary '_h\ğkœ´\Åw\ö\Ó&');
INSERT INTO `build_auth_key` VALUES (50467,'Win','x64','WoW',_binary '^™k\Ü\îhC-c@h\Ñ\ë');
INSERT INTO `build_auth_key` VALUES (50469,'Win','x64','WoW',_binary 'hÌ¶X«;\ï©\Ö£“¢');
INSERT INTO `build_auth_key` VALUES (50504,'Win','x64','WoW',_binary '}_\Ò2É¯]\ÖT3³‘Ôœ');
INSERT INTO `build_auth_key` VALUES (50585,'Win','x64','WoW',_binary '\Ä\÷\Ì8£¸I5¤…\÷\í­>vK');
INSERT INTO `build_auth_key` VALUES (50622,'Win','x64','WoW',_binary '\Ò:&ıuıš`s\ëp`ª(\æ§');
INSERT INTO `build_auth_key` VALUES (50747,'Win','x64','WoW',_binary '-<8jœE\Âs\í:<n³\÷\È');
INSERT INTO `build_auth_key` VALUES (50791,'Win','x64','WoW',_binary '\çĞ»\ï7\Â\\¼h$		 ');
INSERT INTO `build_auth_key` VALUES (51130,'Win','x64','WoW',_binary 'D\Í,‘\ä\ğe]£‡H7&\Î@5');
INSERT INTO `build_auth_key` VALUES (51187,'Win','x64','WoW',_binary 't\â]9e&”Gµ\Ë1üq\Æ');
INSERT INTO `build_auth_key` VALUES (51237,'Win','x64','WoW',_binary '\Èf\n!·fdo½g\ô\Ï\ÏU\Ã');
INSERT INTO `build_auth_key` VALUES (51261,'Win','x64','WoW',_binary '\ëµz\äP3ŸŒ0\Z§‡o«');
INSERT INTO `build_auth_key` VALUES (51313,'Win','x64','WoW',_binary '5AĞ«s\\\÷ …E\Ü0');
INSERT INTO `build_auth_key` VALUES (51421,'Win','x64','WoW',_binary 'E\âMo35&—‡\ß+ c“');
INSERT INTO `build_auth_key` VALUES (51485,'Win','x64','WoW',_binary '\ìT\n]\Ø\\\ç©\Ù;}\Æ\Ñ');
INSERT INTO `build_auth_key` VALUES (51536,'Win','x64','WoW',_binary 'W\ê¨\ìœ?ùbc[´');
INSERT INTO `build_auth_key` VALUES (51754,'Win','x64','WoW',_binary '¾Õ¨aÀq«Aş\ö~7»\Z');
INSERT INTO `build_auth_key` VALUES (51886,'Win','x64','WoW',_binary '	Ï‰ı.«Ş®¼Sµ');
INSERT INTO `build_auth_key` VALUES (51972,'Win','x64','WoW',_binary 'DM\Ç\ï5D¶gˆM­ (');
INSERT INTO `build_auth_key` VALUES (52038,'Win','x64','WoW',_binary '¨\ï\0J\ŞØ£¯\õ¦}+¸\ÙW•');
INSERT INTO `build_auth_key` VALUES (52068,'Win','x64','WoW',_binary '¤O„+¬\Ì~\è\â—_¯\ñ$t');
INSERT INTO `build_auth_key` VALUES (52095,'Win','x64','WoW',_binary 'º68(‡\Ñm\'N©–•\ğ\É\È');
INSERT INTO `build_auth_key` VALUES (52106,'Win','x64','WoW',_binary '•\ô8i·Ø!,¼¸\ó“\í');
INSERT INTO `build_auth_key` VALUES (52129,'Win','x64','WoW',_binary 'İ„/*qb\î¸ı[2Vø');
INSERT INTO `build_auth_key` VALUES (52148,'Win','x64','WoW',_binary 'Š–—\È\Í\Æ\çÿLT\Õ\Ë\0\Â$');
INSERT INTO `build_auth_key` VALUES (52188,'Win','x64','WoW',_binary '—}ù™>”…]\í^2‹§¢\ò');
INSERT INTO `build_auth_key` VALUES (52301,'Win','x64','WoW',_binary '‚\Z£»#{@‚\ôIp%9ª');
INSERT INTO `build_auth_key` VALUES (52393,'Win','x64','WoW',_binary '°\í#·\ïQ²šEYM›»\r');
INSERT INTO `build_auth_key` VALUES (52485,'Win','x64','WoW',_binary 'XÎ´e0®H’X\İ0\ãDA');
INSERT INTO `build_auth_key` VALUES (52545,'Win','x64','WoW',_binary 'ûRšƒU¤n\ÛûÜ\\\Úı');
INSERT INTO `build_auth_key` VALUES (52607,'Win','x64','WoW',_binary '\0.J­\Ê\ê»«Âˆ1­`');
INSERT INTO `build_auth_key` VALUES (52649,'Win','x64','WoW',_binary 'Ğ·yû\ì\ëÁ\íZ…\Ø?È§[');
INSERT INTO `build_auth_key` VALUES (52808,'Win','x64','WoW',_binary 'bvq+lŠ\ê!\Í]”\Õ/\îp\î');
INSERT INTO `build_auth_key` VALUES (52902,'Win','x64','WoW',_binary '\Ô\ğ¢L\ßV(SŒ8z2j\ó');
INSERT INTO `build_auth_key` VALUES (52968,'Win','x64','WoW',_binary '-$\Ô@\ÄMOø[‡ \ò');
INSERT INTO `build_auth_key` VALUES (52983,'Win','x64','WoW',_binary '±å­¥ı\Ğlšµ\åØ¦˜3$¬');
INSERT INTO `build_auth_key` VALUES (53007,'Win','x64','WoW',_binary '¢\ZûM8V¯G™BXÀ\î\õ');
INSERT INTO `build_auth_key` VALUES (53040,'Win','x64','WoW',_binary '/ƒ¿{0{pÛ½u\ÌB\×\Ã');
INSERT INTO `build_auth_key` VALUES (53104,'Win','x64','WoW',_binary '\Û×\È\ßKSÇ‰1¹…Ê´');
INSERT INTO `build_auth_key` VALUES (53162,'Win','x64','WoW',_binary 'ŠgQ¿‰„\î\â¶0\÷\Ë#7j');
INSERT INTO `build_auth_key` VALUES (53212,'Win','x64','WoW',_binary 'vÿ/›c“d¹©ûÿÿ¹I');
INSERT INTO `build_auth_key` VALUES (53262,'Win','x64','WoW',_binary 'aJr\Õ1&4ŠI\'\ìSı+z');
INSERT INTO `build_auth_key` VALUES (53441,'Win','x64','WoW',_binary '¿\İ}\è}_u\æŞ´\õÉ|™');
INSERT INTO `build_auth_key` VALUES (53584,'Win','x64','WoW',_binary '\Í×©6Y 4`µ¦\ÎJ\ÎUT');
INSERT INTO `build_auth_key` VALUES (53840,'Win','x64','WoW',_binary '¬—\×E\Æ\r\Ó\Ü_—>UÀ\ãd');
INSERT INTO `build_auth_key` VALUES (53877,'Win','x64','WoW',_binary '2•¶8F¢\'n&\ÃJ\Ô');
INSERT INTO `build_auth_key` VALUES (53913,'Win','x64','WoW',_binary 'GV€h!’\ë\ÏgD\ÑOuQ™');
INSERT INTO `build_auth_key` VALUES (53989,'Win','x64','WoW',_binary ':ë¬¹á‹ˆº!\õ-Q¸W');
INSERT INTO `build_auth_key` VALUES (54070,'Win','x64','WoW',_binary 'ı ‚d·XrP\Ïxù¹`!i');
INSERT INTO `build_auth_key` VALUES (54205,'Win','x64','WoW',_binary 'XY\ë?\rmw\Â]“\÷\İ');
INSERT INTO `build_auth_key` VALUES (54358,'Win','x64','WoW',_binary '‰\Ç3\Óf\èEû’d@LÔŒÊ‰');
INSERT INTO `build_auth_key` VALUES (54499,'Win','x64','WoW',_binary 'jÁ\ö\ÄÃ\÷‚Û“;\Ø3&');
INSERT INTO `build_auth_key` VALUES (54577,'Win','x64','WoW',_binary 'Ë»»ÿ²\ÆR\ğ\ô§„†\á°c');
INSERT INTO `build_auth_key` VALUES (54601,'Win','x64','WoW',_binary 'ø¸f\Ï}¹¡\"s\ó[9)bsu');
INSERT INTO `build_auth_key` VALUES (54604,'Win','x64','WoW',_binary '¥ÿL\å5\öMcŒ²Á\"C');
INSERT INTO `build_auth_key` VALUES (54630,'Win','x64','WoW',_binary '\óy5Q¦µY6<\ñ‚0');
INSERT INTO `build_auth_key` VALUES (54673,'Win','x64','WoW',_binary '¯‹œŠœa(I…\"‹–Z\0M');
INSERT INTO `build_auth_key` VALUES (54717,'Win','x64','WoW',_binary '†+ºÿkV¼\÷4\Ô\Z');
INSERT INTO `build_auth_key` VALUES (54736,'Win','x64','WoW',_binary 'şX\ğ9\Ù$ˆ†¢‚\'\Ê/');
INSERT INTO `build_auth_key` VALUES (54762,'Win','x64','WoW',_binary '_\ÚV\00%TE·Ej(lú');
INSERT INTO `build_auth_key` VALUES (54847,'Win','x64','WoW',_binary '2ı 51`\ìQ\ë6\\ú\Î\ò·');
INSERT INTO `build_auth_key` VALUES (54904,'Win','x64','WoW',_binary '	\Ë\ì<\r\ê(¼§ŸÙ¸y°\ã');
INSERT INTO `build_auth_key` VALUES (54988,'Win','x64','WoW',_binary '\'‘U2&‡w´ù\×\ñs');
INSERT INTO `build_auth_key` VALUES (55142,'Win','x64','WoW',_binary ')‹™µ­g\ê=*´³\àzAS');
INSERT INTO `build_auth_key` VALUES (55165,'Win','x64','WoW',_binary '&\Ô\ÑÀ4¨¦r=Ê¼”Ko\Ğ');
INSERT INTO `build_auth_key` VALUES (55261,'Win','x64','WoW',_binary '„\áS£\İ‚I\'K‘z¬z\í');
INSERT INTO `build_auth_key` VALUES (55461,'Win','x64','WoW',_binary '\ÄR&tøT\ï\ì\Çl‰\ÉTP™');
INSERT INTO `build_auth_key` VALUES (55664,'Win','x64','WoW',_binary '\Û\ÊXHoª şT\ê(z0G\é#');
INSERT INTO `build_auth_key` VALUES (55666,'Win','x64','WoW',_binary '\÷å¨M6¶RÈ¹\×na|');
INSERT INTO `build_auth_key` VALUES (55792,'Win','x64','WoW',_binary '\Ä\ŞÍªD¼T\ğ\ó»ƒ}!G');
INSERT INTO `build_auth_key` VALUES (55793,'Win','x64','WoW',_binary 'ù\Ï22­8\Â†hÕ»d');
INSERT INTO `build_auth_key` VALUES (55818,'Win','x64','WoW',_binary ':›TbH\÷\ÙÙ°j,$');
INSERT INTO `build_auth_key` VALUES (55824,'Win','x64','WoW',_binary 'Šo&š(–zˆxŸ´§');
INSERT INTO `build_auth_key` VALUES (55846,'Win','x64','WoW',_binary '»^\ípXr\Â&ƒK•©\éø©');
INSERT INTO `build_auth_key` VALUES (55933,'Win','x64','WoW',_binary '\ÃMB\Ë5@\r\Ò!|\ñ');
INSERT INTO `build_auth_key` VALUES (55939,'Win','x64','WoW',_binary '‘RŸL\ä\ä\åN&`¬Ü­\Å');
INSERT INTO `build_auth_key` VALUES (55959,'Win','x64','WoW',_binary '\íW\àS\ÑÎ™±q¾4‚1H©');
INSERT INTO `build_auth_key` VALUES (55960,'Win','x64','WoW',_binary 'Œlü{7\Ş¯0l¯');
INSERT INTO `build_auth_key` VALUES (56008,'Win','x64','WoW',_binary '\0üÖ¿üÌºzq\ã\ên½');
INSERT INTO `build_auth_key` VALUES (56110,'Win','x64','WoW',_binary '€\Ê\"/®7\í<	\â:');
INSERT INTO `build_auth_key` VALUES (56162,'Win','x64','WoW',_binary 'o\\“ºCfe©M¶|\å6N¨');
INSERT INTO `build_auth_key` VALUES (56196,'Mac','A64','WoW',_binary 'wj]\÷šN\ñ¸oe;0<\ç');
INSERT INTO `build_auth_key` VALUES (56196,'Win','x64','WoW',_binary 'ü\'KÿGÿùÈ±ªk\í›');
INSERT INTO `build_auth_key` VALUES (56263,'Win','x64','WoW',_binary '\ê„rÉ’6\Ìa{\õ©$MV');
INSERT INTO `build_auth_key` VALUES (56288,'Mac','A64','WoW',_binary 'Aqy>\ğ!r°nÁ‰m?');
INSERT INTO `build_auth_key` VALUES (56288,'Win','x64','WoW',_binary 'TN5\ĞH	8\åÁ#‹:&½\İ');
INSERT INTO `build_auth_key` VALUES (56311,'Mac','A64','WoW',_binary 'A-2\0qZ¯\Ü\"\ß\Z”');
INSERT INTO `build_auth_key` VALUES (56311,'Win','x64','WoW',_binary '¬dWZ\îM2s_ú\Ğ\År&²');
INSERT INTO `build_auth_key` VALUES (56313,'Mac','A64','WoW',_binary '¨>Ñ\æY¼•\Ì2-I½\í»');
INSERT INTO `build_auth_key` VALUES (56313,'Win','x64','WoW',_binary '\ê\÷i\0ššÓ±ª\á\ÃDK');
INSERT INTO `build_auth_key` VALUES (56380,'Win','x64','WoW',_binary 'ĞxD“#>(i“?\Ì\ñ£Á');
INSERT INTO `build_auth_key` VALUES (56380,'Win','x64','WoWC',_binary 'O³\×-‰\õÿ\İN„”¤\æ');
INSERT INTO `build_auth_key` VALUES (56382,'Win','x64','WoW',_binary 'g\Ãj>\öUSLX­P”!’¿\Ú');
INSERT INTO `build_auth_key` VALUES (56382,'Win','x64','WoWC',_binary '¥½z3ŸX\ÍE\Í\\~½\ñ\"');
INSERT INTO `build_auth_key` VALUES (56421,'Mac','A64','WoW',_binary 'X’ÿ«®ı\Î\Ëÿª¥]/›');
INSERT INTO `build_auth_key` VALUES (56421,'Mac','A64','WoWC',_binary 'b–f\Ö\Ù\ïşKsÁ\í\×F8Şª');
INSERT INTO `build_auth_key` VALUES (56421,'Mac','x64','WoW',_binary 'O\Í\÷ƒÄ„¿EÉƒ\'\Şb');
INSERT INTO `build_auth_key` VALUES (56421,'Mac','x64','WoWC',_binary 'Ka\ä\ô\ó² ÿ[H¦˜ûB');
INSERT INTO `build_auth_key` VALUES (56421,'Win','A64','WoW',_binary 'ˆˆ\Ç`p|\ÉÁÁÖ´[H8');
INSERT INTO `build_auth_key` VALUES (56421,'Win','x64','WoW',_binary ';ß©ªKp,‹Œ\Ş<\ÂU');
INSERT INTO `build_auth_key` VALUES (56421,'Win','x64','WoWC',_binary '\rÛ‡8d<\ØıXZ:x\í');
INSERT INTO `build_auth_key` VALUES (56461,'Mac','A64','WoW',_binary '\å\æ\ê\äS\"™\öH°±');
INSERT INTO `build_auth_key` VALUES (56461,'Mac','A64','WoWC',_binary '…D|Ö•\Â\Ü/a‹#™kJP<');
INSERT INTO `build_auth_key` VALUES (56461,'Mac','x64','WoW',_binary '\âhx\ß47x/n“\è3´Í„:');
INSERT INTO `build_auth_key` VALUES (56461,'Mac','x64','WoWC',_binary '‹`°\İ\ÕYg?\ÑÁ\Ú\åˆY');
INSERT INTO `build_auth_key` VALUES (56461,'Win','A64','WoW',_binary '/†*·$	+™È¡\ö§µ?j');
INSERT INTO `build_auth_key` VALUES (56461,'Win','x64','WoW',_binary '<Ø0\Ä\Õ\Ã\Û\æ\î\ËG´');
INSERT INTO `build_auth_key` VALUES (56461,'Win','x64','WoWC',_binary 'y½Lˆ/\r¤&\×j7\"\Ì');
INSERT INTO `build_auth_key` VALUES (56513,'Mac','A64','WoW',_binary '\Ç\çW\à\Şga’QùB¶ÉŠS·');
INSERT INTO `build_auth_key` VALUES (56513,'Mac','A64','WoWC',_binary '<ÀdF\ÕW\ÄÙ¦%\n¨Õ³‡');
INSERT INTO `build_auth_key` VALUES (56513,'Mac','x64','WoW',_binary '\ÑD8$\ã{s\ËÙ“\Î-ú ˜');
INSERT INTO `build_auth_key` VALUES (56513,'Mac','x64','WoWC',_binary '\êÙ ·a\Ä\É\Èú\ÕL\à\ÙkF');
INSERT INTO `build_auth_key` VALUES (56513,'Win','A64','WoW',_binary 'u4h\÷S\èÓ„]k0');
INSERT INTO `build_auth_key` VALUES (56513,'Win','x64','WoW',_binary '›\Ø\Ñœ\ÆEŸ\ã.\Ç	š¶');
INSERT INTO `build_auth_key` VALUES (56513,'Win','x64','WoWC',_binary '»Z5\Â\ën &ÿ\àı\ë\Ö\é');
INSERT INTO `build_auth_key` VALUES (56625,'Mac','A64','WoW',_binary 'Ç­—¹—`Ğ½\çª\îø~-');
INSERT INTO `build_auth_key` VALUES (56625,'Mac','A64','WoWC',_binary 'Ş½´X—Vs^\ö3r&\å');
INSERT INTO `build_auth_key` VALUES (56625,'Mac','x64','WoW',_binary '\ëVeı\È58”K¨.CZS\æ');
INSERT INTO `build_auth_key` VALUES (56625,'Mac','x64','WoWC',_binary 'ª[9£\Ñ<‰\ó\ÙFpX6');
INSERT INTO `build_auth_key` VALUES (56625,'Win','A64','WoW',_binary 'hAS‡>P#g…²pÚ‡–h');
INSERT INTO `build_auth_key` VALUES (56625,'Win','x64','WoW',_binary '€…œ#\Ã\çø\'‚G ŠŒ’');
INSERT INTO `build_auth_key` VALUES (56625,'Win','x64','WoWC',_binary '`i\í!\"wıÛº\ä›\æ\èÌ¯');
INSERT INTO `build_auth_key` VALUES (56647,'Mac','A64','WoW',_binary 'µ¢]\ïŒ]<ty\ä\â');
INSERT INTO `build_auth_key` VALUES (56647,'Mac','A64','WoWC',_binary '\ğ\ò¾\Ã\ŞY\nç¥œ\Ï\ÛB _');
INSERT INTO `build_auth_key` VALUES (56647,'Mac','x64','WoW',_binary '*‚\\|\õU(¥\í\è}j\ä');
INSERT INTO `build_auth_key` VALUES (56647,'Win','A64','WoW',_binary '6Äh\ãXYƒ\ğ³\ß\Ëa');
INSERT INTO `build_auth_key` VALUES (56647,'Win','x64','WoW',_binary '.œEkfÀ\Õ\Ê\Ö\èız/\êz');
INSERT INTO `build_auth_key` VALUES (56647,'Win','x64','WoWC',_binary 'T:i«€-s\ØJ°\ìMŒ¬');
INSERT INTO `build_auth_key` VALUES (56819,'Mac','A64','WoW',_binary 'S\Í!Zb\âÜ’\Ía\Äû]ù\Ã');
INSERT INTO `build_auth_key` VALUES (56819,'Mac','A64','WoWC',_binary '~\ğ]4 dù1\å2d4—');
INSERT INTO `build_auth_key` VALUES (56819,'Mac','x64','WoW',_binary '[~^Y\åJ;–\åÌºn\ôe');
INSERT INTO `build_auth_key` VALUES (56819,'Mac','x64','WoWC',_binary '+\Ü\ÙRº¤~l½qÇ€¼Œ\ÍG');
INSERT INTO `build_auth_key` VALUES (56819,'Win','A64','WoW',_binary '}\ÇRG›V„¸(›Åµ\È_?');
INSERT INTO `build_auth_key` VALUES (56819,'Win','x64','WoW',_binary 'Cs\ô^av63%ig\Õ\Õ\é');
INSERT INTO `build_auth_key` VALUES (56819,'Win','x64','WoWC',_binary '¹\ä\"\ßv\Úc^;u½T­\Ø\Ş');
INSERT INTO `build_auth_key` VALUES (57171,'Mac','A64','WoW',_binary 'C\'(Šˆ\ë\Ü¾X\ò{£i\æ');
INSERT INTO `build_auth_key` VALUES (57171,'Mac','A64','WoWC',_binary '\"y&Q8ùQo8,¡`Ê½±(');
INSERT INTO `build_auth_key` VALUES (57171,'Mac','x64','WoW',_binary '(\Õ#|%1\Î\ZlT\r');
INSERT INTO `build_auth_key` VALUES (57171,'Mac','x64','WoWC',_binary '6³±©^2‰4¤7\Ã');
INSERT INTO `build_auth_key` VALUES (57171,'Win','A64','WoW',_binary 'ŸR±\ñD\Ú8_Œo‘g\ĞW');
INSERT INTO `build_auth_key` VALUES (57171,'Win','x64','WoW',_binary '–{™P<]z‹G”\ö	H');
INSERT INTO `build_auth_key` VALUES (57171,'Win','x64','WoWC',_binary '>\æ(\ò6\Úd,°¸bL„™');
INSERT INTO `build_auth_key` VALUES (57212,'Mac','A64','WoW',_binary 'Z\ğ¯²Aú¢¯t…úbª	N');
INSERT INTO `build_auth_key` VALUES (57212,'Mac','A64','WoWC',_binary '¤±¶ºz\ìZ\ïb/;\Ø');
INSERT INTO `build_auth_key` VALUES (57212,'Mac','x64','WoW',_binary 'ûY\ä¬o\ö‘¨z ik:\ÄN');
INSERT INTO `build_auth_key` VALUES (57212,'Mac','x64','WoWC',_binary '2\ë;µ\ê`\îÄ”_q\ÛÒ´\ó');
INSERT INTO `build_auth_key` VALUES (57212,'Win','A64','WoW',_binary '¡\ÙI\Ğb\ìIå¢—‹Ñ“,');
INSERT INTO `build_auth_key` VALUES (57212,'Win','x64','WoW',_binary '¸­µ}\'\ï˜\È\İ\ï™C¾\î”');
INSERT INTO `build_auth_key` VALUES (57212,'Win','x64','WoWC',_binary 'Ñ‹y\Î\óK^ºŒwşm ');
INSERT INTO `build_auth_key` VALUES (57292,'Mac','A64','WoW',_binary '«H\Í\ÓD”@\ê¦\ç-C‘J8$');
INSERT INTO `build_auth_key` VALUES (57292,'Mac','A64','WoWC',_binary 'F\æü9+hk\Ü\É*m');
INSERT INTO `build_auth_key` VALUES (57292,'Mac','x64','WoW',_binary '7[4Vq’P<\Éù\È\Æ\ó');
INSERT INTO `build_auth_key` VALUES (57292,'Mac','x64','WoWC',_binary '\åŸu0Ç¿\÷|¶.b\Ô\æ\Ó\î\È');
INSERT INTO `build_auth_key` VALUES (57292,'Win','A64','WoW',_binary '\ô+†-Y\Ì\Ó K-\õ™Wa');
INSERT INTO `build_auth_key` VALUES (57292,'Win','x64','WoW',_binary '\íƒS2}U8\÷_¯.XM.\ó');
INSERT INTO `build_auth_key` VALUES (57292,'Win','x64','WoWC',_binary 'güVÌ–\Æ\ô¬\â‘Q7Yb\ô');
INSERT INTO `build_auth_key` VALUES (57388,'Mac','A64','WoW',_binary 'úo@c”Š·O³ı\ã5À\Õ');
INSERT INTO `build_auth_key` VALUES (57388,'Mac','A64','WoWC',_binary '9b—E\õ~\ó;»\Ô\É¨!W');
INSERT INTO `build_auth_key` VALUES (57388,'Mac','x64','WoW',_binary '\ç\ãC#W¼cN½{vº@\Õ\Ò');
INSERT INTO `build_auth_key` VALUES (57388,'Mac','x64','WoWC',_binary '¿ı\ÔM‰*!C¶µD4Œ');
INSERT INTO `build_auth_key` VALUES (57388,'Win','A64','WoW',_binary '’\é-+¥\öu\Å\Şd+‰');
INSERT INTO `build_auth_key` VALUES (57388,'Win','x64','WoW',_binary 'ù\ËrLf.>€)¤BD<c');
INSERT INTO `build_auth_key` VALUES (57388,'Win','x64','WoWC',_binary '\ßÙ¿ı\0û\ï\ì¿\n\ïü-');
INSERT INTO `build_auth_key` VALUES (57534,'Mac','A64','WoW',_binary ';‚>‰‘ \ß*\ã=f\Z``');
INSERT INTO `build_auth_key` VALUES (57534,'Mac','A64','WoWC',_binary 'È‚EY\Ü$…§\ã½	qùŠ');
INSERT INTO `build_auth_key` VALUES (57534,'Mac','x64','WoW',_binary '|gª­@ª«ƒ‹%\\\ß');
INSERT INTO `build_auth_key` VALUES (57534,'Mac','x64','WoWC',_binary 'ÎœÙ +\ğÃ±&\ó\Ç\á¿r(');
INSERT INTO `build_auth_key` VALUES (57534,'Win','A64','WoW',_binary '\Ìa/…\ñXÈ²s}ı’ec');
INSERT INTO `build_auth_key` VALUES (57534,'Win','x64','WoW',_binary '”ˆW¿v¢\ÒLZÑ‹O/\Ï');
INSERT INTO `build_auth_key` VALUES (57534,'Win','x64','WoWC',_binary 'b°*E\Â`h]D\"Â£');
INSERT INTO `build_auth_key` VALUES (57637,'Mac','A64','WoW',_binary '%\Ûh+³µm³xDh•n');
INSERT INTO `build_auth_key` VALUES (57637,'Mac','A64','WoWC',_binary 'r/\ÊCZ\á\é¤\Ùnß\Üû;');
INSERT INTO `build_auth_key` VALUES (57637,'Mac','x64','WoW',_binary 'Ë²U\å1ø\Ë\ë	&¸\âú');
INSERT INTO `build_auth_key` VALUES (57637,'Mac','x64','WoWC',_binary '¦~\Ërh·şuœ\éO[2');
INSERT INTO `build_auth_key` VALUES (57637,'Win','A64','WoW',_binary 'K9\ÇJp5¶¡Ç™\ğ\ôüg5\Ò');
INSERT INTO `build_auth_key` VALUES (57637,'Win','x64','WoW',_binary 'lA…\és\ÉËƒ‚\Ór\Z\ñf');
INSERT INTO `build_auth_key` VALUES (57637,'Win','x64','WoWC',_binary 'qµxr9<+Ü»\İ\Ä:&');
INSERT INTO `build_auth_key` VALUES (57689,'Mac','A64','WoW',_binary ']OdÂ…\ğø\Ü\éŠl\Å');
INSERT INTO `build_auth_key` VALUES (57689,'Mac','A64','WoWC',_binary '«=ø·Ÿ[®ƒ—d˜\æS');
INSERT INTO `build_auth_key` VALUES (57689,'Mac','x64','WoW',_binary '˜„YyK80P¾B†ùy¢');
INSERT INTO `build_auth_key` VALUES (57689,'Mac','x64','WoWC',_binary '\Êz\ÙAø\â<\â\'\Ôu$r');
INSERT INTO `build_auth_key` VALUES (57689,'Win','A64','WoW',_binary 'I\â*!ai4\ÍMqF‘tŒ');
INSERT INTO `build_auth_key` VALUES (57689,'Win','x64','WoW',_binary '“´\åe\Z(\Ø\ÌN|\çDy\ö');
INSERT INTO `build_auth_key` VALUES (57689,'Win','x64','WoWC',_binary 'j\í\Ø%N—\÷7Ë¨RrŠt');
INSERT INTO `build_auth_key` VALUES (58123,'Mac','A64','WoW',_binary '|cÎŠ‰@\Ô\Ğ\ÇÏ™=\Ç');
INSERT INTO `build_auth_key` VALUES (58123,'Mac','A64','WoWC',_binary '\Ï&%0\r©Ì›m7œXµ');
INSERT INTO `build_auth_key` VALUES (58123,'Mac','x64','WoW',_binary 'h˜`¦\ë$¾ª\Ğ\"³¹');
INSERT INTO `build_auth_key` VALUES (58123,'Mac','x64','WoWC',_binary '07ûjs\÷F\Ñ€\0±3');
INSERT INTO `build_auth_key` VALUES (58123,'Win','A64','WoW',_binary '„·\\\ó€Ã³—¶:ûIb');
INSERT INTO `build_auth_key` VALUES (58123,'Win','x64','WoW',_binary ')\n \Ö.ù–4Ì·µù\ö¬');
INSERT INTO `build_auth_key` VALUES (58123,'Win','x64','WoWC',_binary '-^\î«W¯ş1…m\r\ê\Ç9D\Ú');
INSERT INTO `build_auth_key` VALUES (58162,'Mac','A64','WoW',_binary 'k¹:ù\'‘\æÍ¥Š_ ªÁ');
INSERT INTO `build_auth_key` VALUES (58162,'Mac','A64','WoWC',_binary '(Ó¤\ò#\á\Ø\óÏ‰;¿®û\Å');
INSERT INTO `build_auth_key` VALUES (58162,'Mac','x64','WoW',_binary 'GXk2½Uo Œ–mO\Éÿ$');
INSERT INTO `build_auth_key` VALUES (58162,'Mac','x64','WoWC',_binary '\èa1Vs—,˜fPÁ$!–ş');
INSERT INTO `build_auth_key` VALUES (58162,'Win','A64','WoW',_binary '[—\Ô7b\ğ\ñ¾\Ç^™\ÍÑ5');
INSERT INTO `build_auth_key` VALUES (58162,'Win','x64','WoW',_binary '\Ú;H¹¦D†\ò{©Ø…Vˆ');
INSERT INTO `build_auth_key` VALUES (58162,'Win','x64','WoWC',_binary '%n\×q\ÜbŸ+Œ\õ&ºV,');
INSERT INTO `build_auth_key` VALUES (58187,'Mac','A64','WoW',_binary 'ˆÙ“’mw}Á Y˜\0šs\Ö');
INSERT INTO `build_auth_key` VALUES (58187,'Mac','A64','WoWC',_binary 'yO\Õ\Ív\ï\ô\0ƒs8‡k¦O');
INSERT INTO `build_auth_key` VALUES (58187,'Mac','x64','WoW',_binary '\Şw )CÁV	ø„ÿf');
INSERT INTO `build_auth_key` VALUES (58187,'Mac','x64','WoWC',_binary '\çw}\Î\ÛE·\í>z\ß#¼±\Ë');
INSERT INTO `build_auth_key` VALUES (58187,'Win','A64','WoW',_binary 'ÿ\êÈ±µGı\ïÊ‹\Ï0ßŸQ');
INSERT INTO `build_auth_key` VALUES (58187,'Win','x64','WoW',_binary '\Ù,5ÖK	E™+\å8\"\ç');
INSERT INTO `build_auth_key` VALUES (58187,'Win','x64','WoWC',_binary 'Š\Â\Æ@5Ÿ\'l/aSú¢†8');
INSERT INTO `build_auth_key` VALUES (58238,'Mac','A64','WoW',_binary '‡pQv\äÒœ\×Ü¤z=¸²\é');
INSERT INTO `build_auth_key` VALUES (58238,'Mac','A64','WoWC',_binary '\ìr·@\ÑbB›J\Ì\Æ7,=');
INSERT INTO `build_auth_key` VALUES (58238,'Mac','x64','WoW',_binary '\Z@…+/^\î\â\ë€I{iˆ¯');
INSERT INTO `build_auth_key` VALUES (58238,'Mac','x64','WoWC',_binary 'H\âR!\æøL¤–…¹–\á4Z');
INSERT INTO `build_auth_key` VALUES (58238,'Win','A64','WoW',_binary 'j¦\ğ‚\å5\êp\äÅ½…{');
INSERT INTO `build_auth_key` VALUES (58238,'Win','x64','WoW',_binary '‘\ö\Ò<b\ğ\ËSNKªOÁ');
INSERT INTO `build_auth_key` VALUES (58238,'Win','x64','WoWC',_binary '{J­É¥˜¤”\Ö^©\\');
INSERT INTO `build_auth_key` VALUES (58533,'Mac','A64','WoW',_binary '\æ7ª2\ÕŞ‘Kš\öŒ\ÉÉº«');
INSERT INTO `build_auth_key` VALUES (58533,'Mac','A64','WoWC',_binary '\æ“\İ\'\\\î\âÖ‚\ì—=y%w');
INSERT INTO `build_auth_key` VALUES (58533,'Mac','x64','WoW',_binary '\Ş7#\ï~¹‘¡²v„\æQ94\Ö');
INSERT INTO `build_auth_key` VALUES (58533,'Mac','x64','WoWC',_binary '©>\ô8Qt\0i\ô?\ğ\ß4W');
INSERT INTO `build_auth_key` VALUES (58533,'Win','A64','WoW',_binary '8#\Ò\ámn1H„\ÍXiIRO,');
INSERT INTO `build_auth_key` VALUES (58533,'Win','x64','WoW',_binary 'I\nOº	s\à¶*H\æd');
INSERT INTO `build_auth_key` VALUES (58533,'Win','x64','WoWC',_binary '=\É\â/U=c\Z\\ g\÷€š=-');
INSERT INTO `build_auth_key` VALUES (58608,'Mac','A64','WoW',_binary ':;c›\ÃD\í´1H`c\ñ');
INSERT INTO `build_auth_key` VALUES (58608,'Mac','A64','WoWC',_binary 'FD\íøk\Ô]#°\ò<\ã‹');
INSERT INTO `build_auth_key` VALUES (58608,'Mac','x64','WoW',_binary ')\ìø’\ñš\Ùp{\'$­\Ú');
INSERT INTO `build_auth_key` VALUES (58608,'Mac','x64','WoWC',_binary '+\õa=‚­:$+]Ğ±@\ÎD');
INSERT INTO `build_auth_key` VALUES (58608,'Win','A64','WoW',_binary 'oØ­|\Ë;}­E\\©=:h\ó');
INSERT INTO `build_auth_key` VALUES (58608,'Win','x64','WoW',_binary '!³\í\ë\Ze8Õ˜€VÒ¾r²');
INSERT INTO `build_auth_key` VALUES (58608,'Win','x64','WoWC',_binary '¬e\äÜ†şCb\ç+¼\İthS');
INSERT INTO `build_auth_key` VALUES (58630,'Mac','A64','WoW',_binary 'Z–O;>\'::\Øb\ÅÃŠ');
INSERT INTO `build_auth_key` VALUES (58630,'Mac','A64','WoWC',_binary '<\Ühı{´\å)B(\í‚i');
INSERT INTO `build_auth_key` VALUES (58630,'Mac','x64','WoW',_binary '\rXˆ¥úz\Õ\ÂFl\ë9†');
INSERT INTO `build_auth_key` VALUES (58630,'Mac','x64','WoWC',_binary '—E\çbl\r}\nW\ß±\êO');
INSERT INTO `build_auth_key` VALUES (58630,'Win','A64','WoW',_binary '\÷Jœ¹Ö‘‡€\È\â\î\à\òM\à');
INSERT INTO `build_auth_key` VALUES (58630,'Win','x64','WoW',_binary 'Œœ\Ü\Ç‰.ª\êy=»\ó°\ÈP');
INSERT INTO `build_auth_key` VALUES (58630,'Win','x64','WoWC',_binary 'Æ£c«:˜\ôƒqá½®\Ê');
INSERT INTO `build_auth_key` VALUES (58680,'Mac','A64','WoW',_binary '^\æ«\å8n†d+=ı©\Ô');
INSERT INTO `build_auth_key` VALUES (58680,'Mac','A64','WoWC',_binary '\"\Æ6»J»Ä­š\è\óm”wt');
INSERT INTO `build_auth_key` VALUES (58680,'Mac','x64','WoWC',_binary '¥ÿ!\Ü\İ`µ<ù2s)i');
INSERT INTO `build_auth_key` VALUES (58680,'Win','A64','WoW',_binary 'mÈ¾\ÒeÈ˜ƒe“©\æT¡\á');
INSERT INTO `build_auth_key` VALUES (58680,'Win','x64','WoW',_binary 'Á\ÎÿIŸ,*¿\å_e\Ï\'Œ');
INSERT INTO `build_auth_key` VALUES (58680,'Win','x64','WoWC',_binary '\ro\Ç|80ª¨q©\â\Ë\ã€');
INSERT INTO `build_auth_key` VALUES (58773,'Mac','A64','WoW',_binary 'R\Ôd²ßÀ*Æ˜\óüùÁ');
INSERT INTO `build_auth_key` VALUES (58773,'Mac','A64','WoWC',_binary '\ğ\ë\Õ\ï#§ƒ¯qtä±°\Z');
INSERT INTO `build_auth_key` VALUES (58773,'Mac','x64','WoW',_binary '¬*VÜ±&9\ô?\ô;©©ù–');
INSERT INTO `build_auth_key` VALUES (58773,'Mac','x64','WoWC',_binary '„1\Â\ØZn\ã\áÇ®ƒ\ğ\Ş');
INSERT INTO `build_auth_key` VALUES (58773,'Win','A64','WoW',_binary 'š\ß;p\ßv\ÎÁ–H¤£');
INSERT INTO `build_auth_key` VALUES (58773,'Win','x64','WoW',_binary 'd„t\Ş\é\ñj\ñÀ+\Ö?>›');
INSERT INTO `build_auth_key` VALUES (58773,'Win','x64','WoWC',_binary '6,€\ÄIú\ã\Ì\n\òC\á3z');
INSERT INTO `build_auth_key` VALUES (58867,'Mac','A64','WoW',_binary '¢•‡pß¿Û“8Q¤ª\ÛIP');
INSERT INTO `build_auth_key` VALUES (58867,'Mac','A64','WoWC',_binary '\rtzY1\ß[[\\Ä‰-c\á\Ø');
INSERT INTO `build_auth_key` VALUES (58867,'Mac','x64','WoW',_binary '£im\òª±O4\\C“û‰');
INSERT INTO `build_auth_key` VALUES (58867,'Mac','x64','WoWC',_binary '·\Æ_Á\ÚQ\Õ0@&/\ï\ï¦');
INSERT INTO `build_auth_key` VALUES (58867,'Win','A64','WoW',_binary '\ïŒ\"X\Û\Æ\Ú\\9i›Ş·¡');
INSERT INTO `build_auth_key` VALUES (58867,'Win','x64','WoW',_binary '|\'§2\\Wú\Ò\Z)\Õ{¿€¦');
INSERT INTO `build_auth_key` VALUES (58867,'Win','x64','WoWC',_binary '?å„{dMovH”wT\r');
INSERT INTO `build_auth_key` VALUES (58911,'Mac','A64','WoW',_binary '\Ş\æ}:\İZ”Ç½¡\æW');
INSERT INTO `build_auth_key` VALUES (58911,'Mac','A64','WoWC',_binary 'm<$+<vho\îé±„DÁ\Ç');
INSERT INTO `build_auth_key` VALUES (58911,'Mac','x64','WoW',_binary 'x\ÓWf®\ÓŞ¸\É\nÿƒ;');
INSERT INTO `build_auth_key` VALUES (58911,'Mac','x64','WoWC',_binary '¤\È\Û\È¡Ÿ«ñº¤ƒÛš');
INSERT INTO `build_auth_key` VALUES (58911,'Win','A64','WoW',_binary '\ğ!0º{ÿ,’\ò_<b@²\è');
INSERT INTO `build_auth_key` VALUES (58911,'Win','x64','WoW',_binary '^\óA\Ù\õ¢ym[×†¥½Z');
INSERT INTO `build_auth_key` VALUES (58911,'Win','x64','WoWC',_binary '©À¾¡·5e	gT ©\n');
INSERT INTO `build_auth_key` VALUES (59207,'Mac','A64','WoW',_binary '.\Åê¢§W=€Š~‘]\0');
INSERT INTO `build_auth_key` VALUES (59207,'Mac','A64','WoWC',_binary '#£_\Üf—aş¬“¿ ©&\î');
INSERT INTO `build_auth_key` VALUES (59207,'Mac','x64','WoW',_binary '+—ÿ}\ê‡\éQ\İ\é\Ze\ÂY[');
INSERT INTO `build_auth_key` VALUES (59207,'Mac','x64','WoWC',_binary '_¶\nÜ‚ÆP·	V¿¼w¼');
INSERT INTO `build_auth_key` VALUES (59207,'Win','A64','WoW',_binary 'A¯=Ÿƒ1O\Ä@k\õ\Â; «');
INSERT INTO `build_auth_key` VALUES (59207,'Win','x64','WoW',_binary '´ˆweGß–ÅS');
INSERT INTO `build_auth_key` VALUES (59207,'Win','x64','WoWC',_binary '©‹\æ*\÷A0JL\'8');
INSERT INTO `build_auth_key` VALUES (59302,'Mac','A64','WoW',_binary 'p¦¾\0!;·\n¹lQ(\rrÑ†');
INSERT INTO `build_auth_key` VALUES (59302,'Mac','A64','WoWC',_binary 'j\\ş\r:K\Ş\Ç\Ñm3é¬§\×');
INSERT INTO `build_auth_key` VALUES (59302,'Mac','x64','WoW',_binary 'Ÿ\ÙY\0\nú¢\å\ñ\nJ¾3¬b¾');
INSERT INTO `build_auth_key` VALUES (59302,'Mac','x64','WoWC',_binary 'Ó›Gı4-V\Ë\Â\è_\ÊWM');
INSERT INTO `build_auth_key` VALUES (59302,'Win','A64','WoW',_binary '`±u|§xœš\Ío¶(\Õ');
INSERT INTO `build_auth_key` VALUES (59302,'Win','x64','WoW',_binary '3N~\ñ\à}¨8,6P\×\çµ');
INSERT INTO `build_auth_key` VALUES (59302,'Win','x64','WoWC',_binary 'µ \ÅOk3E\æ‚`\ö3Nu“');
INSERT INTO `build_auth_key` VALUES (59347,'Mac','A64','WoW',_binary 'Ç¸¶ù } \Ì\ëú\ÃoÀs');
INSERT INTO `build_auth_key` VALUES (59347,'Mac','A64','WoWC',_binary 'º¹†¥Û‰Fz¥a:k£E');
INSERT INTO `build_auth_key` VALUES (59347,'Mac','x64','WoW',_binary '\Z413¿\Z½¿\ö²‡³|#');
INSERT INTO `build_auth_key` VALUES (59347,'Mac','x64','WoWC',_binary ':bL7\îÉ·£­E2\Ï');
INSERT INTO `build_auth_key` VALUES (59347,'Win','A64','WoW',_binary '€\ÊÄŸn©šÆŒ©¸4Fl¥\Ü');
INSERT INTO `build_auth_key` VALUES (59347,'Win','x64','WoW',_binary 'c\"ƒ9S•R\ÑhU—ld„‹');
INSERT INTO `build_auth_key` VALUES (59347,'Win','x64','WoWC',_binary '»Eú\Åb\ë|\óüL|\Z\é:\ö');
INSERT INTO `build_auth_key` VALUES (59425,'Mac','A64','WoW',_binary ' ï·Å¤8Î‡é›…');
INSERT INTO `build_auth_key` VALUES (59425,'Mac','A64','WoWC',_binary '8¹¡øv\\’|rLşN’\Â');
INSERT INTO `build_auth_key` VALUES (59425,'Mac','x64','WoW',_binary '#\n|¢å³…\ËN$¥•)');
INSERT INTO `build_auth_key` VALUES (59425,'Mac','x64','WoWC',_binary '°¿‚h‘)0=\Ê\\\ò³ŸP');
INSERT INTO `build_auth_key` VALUES (59425,'Win','A64','WoW',_binary '\ô\í&¢EN1°D±­VK}');
INSERT INTO `build_auth_key` VALUES (59425,'Win','x64','WoW',_binary '_\ã\ã˜\n¡\Ô\íd$ÀÆ“-');
INSERT INTO `build_auth_key` VALUES (59425,'Win','x64','WoWC',_binary '\ì¥\Ú4Jùs°\ÒZÁ?¶”¸');
INSERT INTO `build_auth_key` VALUES (59466,'Mac','A64','WoW',_binary '\öË·«føšÜ¨PR¿®@');
INSERT INTO `build_auth_key` VALUES (59466,'Mac','A64','WoWC',_binary '!1\ì\ïvVü90\Ü\äa„');
INSERT INTO `build_auth_key` VALUES (59466,'Mac','x64','WoW',_binary '™]\'ÿ˜¨sh ‡£\ñ\Ã');
INSERT INTO `build_auth_key` VALUES (59466,'Mac','x64','WoWC',_binary 'Qa#.\Z‹?<§	\ò¨M');
INSERT INTO `build_auth_key` VALUES (59466,'Win','A64','WoW',_binary '\ğ˜l­dŒ{\'sj0Š');
INSERT INTO `build_auth_key` VALUES (59466,'Win','x64','WoW',_binary 'ıgO[Q\Âw\õ\ÈP^”y\Ó');
INSERT INTO `build_auth_key` VALUES (59466,'Win','x64','WoWC',_binary 'Â™\ËAn3.K¾Z¡ª\é¤7');
INSERT INTO `build_auth_key` VALUES (59538,'Mac','A64','WoW',_binary '\Üo]…·j\í\Ìş(u');
INSERT INTO `build_auth_key` VALUES (59538,'Mac','A64','WoWC',_binary '\ãN$\õWú…\î]l±¼bF');
INSERT INTO `build_auth_key` VALUES (59538,'Mac','x64','WoW',_binary '\ËÃ–\Ã\â\0yùÏ‹g\Ü\Û\î');
INSERT INTO `build_auth_key` VALUES (59538,'Mac','x64','WoWC',_binary 'sİ£aŒ Ú§œ ¸”\İ');
INSERT INTO `build_auth_key` VALUES (59538,'Win','A64','WoW',_binary 'I‚°…o¡ 9¨µ\å]\Ó');
INSERT INTO `build_auth_key` VALUES (59538,'Win','x64','WoW',_binary 'r¹\Ì8R>\Z0û\ò= É«');
INSERT INTO `build_auth_key` VALUES (59538,'Win','x64','WoWC',_binary '¡˜¨ÿ00\ê\Ä;tªH›¼');
INSERT INTO `build_auth_key` VALUES (59570,'Mac','A64','WoW',_binary 'c\Æ\Ã\06|@BXM§ø,');
INSERT INTO `build_auth_key` VALUES (59570,'Mac','A64','WoWC',_binary 'oT!3úN\á‰,ÿ\Ñ\"');
INSERT INTO `build_auth_key` VALUES (59570,'Mac','x64','WoW',_binary '–‹¹Ô©\"\Ã4\nw\ã‡');
INSERT INTO `build_auth_key` VALUES (59570,'Mac','x64','WoWC',_binary ')\Ğ\Å\Æ\ãø@N§i0\î’\ç');
INSERT INTO `build_auth_key` VALUES (59570,'Win','A64','WoW',_binary '\Ó\ê]¾\ô•\ä \Z\é‰\è\ôL\Ø');
INSERT INTO `build_auth_key` VALUES (59570,'Win','x64','WoW',_binary 'uq\à\ÃEœ›ı\ÆK\õ¯_');
INSERT INTO `build_auth_key` VALUES (59570,'Win','x64','WoWC',_binary '\Å\Ê\"\ô\ã–W\îA_Ê¥=K#');
INSERT INTO `build_auth_key` VALUES (59679,'Mac','A64','WoW',_binary 'rÆ»%Ô‡Š‘€… Ñ');
INSERT INTO `build_auth_key` VALUES (59679,'Mac','A64','WoWC',_binary 'EË†\0·@KF°2<\ÎK4\n');
INSERT INTO `build_auth_key` VALUES (59679,'Mac','x64','WoW',_binary '.<\Ù\Ø9v»}V\ÓHœ\ä\ry');
INSERT INTO `build_auth_key` VALUES (59679,'Mac','x64','WoWC',_binary 'M(ŞŸ36$11Pøq\Är\ï');
INSERT INTO `build_auth_key` VALUES (59679,'Win','A64','WoW',_binary '\äC^\æä–‹Q\æYk`\î-¶');
INSERT INTO `build_auth_key` VALUES (59679,'Win','x64','WoW',_binary 'T\ä\Å<]Á“”\ŞM\Ğ6_\â');
INSERT INTO `build_auth_key` VALUES (59679,'Win','x64','WoWC',_binary '²;ı™\ëÍ‡.£’0áª¼Ñ›');
INSERT INTO `build_auth_key` VALUES (59888,'Mac','A64','WoW',_binary '¶Bƒÿ\È\Õú^\òhœ');
INSERT INTO `build_auth_key` VALUES (59888,'Mac','A64','WoWC',_binary '¹\à\È\Â\ncÃ·\ê€7¦–Œ');
INSERT INTO `build_auth_key` VALUES (59888,'Mac','x64','WoW',_binary '’\Ö/;°ø¦ÿ+\èV}');
INSERT INTO `build_auth_key` VALUES (59888,'Mac','x64','WoWC',_binary '°\ô¯c\ì\Åç˜¼I‚p\é');
INSERT INTO `build_auth_key` VALUES (59888,'Win','A64','WoW',_binary '{\Ç7F¹&ş¾\ÉüµS');
INSERT INTO `build_auth_key` VALUES (59888,'Win','x64','WoW',_binary '¶¢\ÄtRW*¿’\0Á\Ê’');
INSERT INTO `build_auth_key` VALUES (59888,'Win','x64','WoWC',_binary 'pš\Ö\õ‘™MAË¡\ôŸ!#');
INSERT INTO `build_auth_key` VALUES (60037,'Mac','A64','WoW',_binary '-Ÿ\çnj“\İ“\ß\r=‹/­');
INSERT INTO `build_auth_key` VALUES (60037,'Mac','A64','WoWC',_binary 'z\à\å€M\×\İ]p{\çM²\È');
INSERT INTO `build_auth_key` VALUES (60037,'Mac','x64','WoW',_binary '¼\ĞQQ<Z·‹\÷°¨\ì\å\ì');
INSERT INTO `build_auth_key` VALUES (60037,'Mac','x64','WoWC',_binary 'd\çmùœi\ØReiy‚†\0\Î');
INSERT INTO `build_auth_key` VALUES (60037,'Win','A64','WoW',_binary '*I9\ì„@«—\é(*,\Î>$');
INSERT INTO `build_auth_key` VALUES (60037,'Win','x64','WoW',_binary 'GO\õ.\ô}GR\ñ8)Cr=');
INSERT INTO `build_auth_key` VALUES (60037,'Win','x64','WoWC',_binary 'u\ë½<%—\ä£’K^');
INSERT INTO `build_auth_key` VALUES (60189,'Mac','A64','WoW',_binary '|S.MD)C\rT»\ÕØƒ6');
INSERT INTO `build_auth_key` VALUES (60189,'Mac','A64','WoWC',_binary '\ÏQ\Ò#Ô‹$oÁ:“v¨\É3\õ');
INSERT INTO `build_auth_key` VALUES (60189,'Mac','x64','WoW',_binary 'YµnPÒ¡\Ó\ë(2_W¿ct(');
INSERT INTO `build_auth_key` VALUES (60189,'Mac','x64','WoWC',_binary '@\Îfv\ÜG\ç}k\íhm1B›\æ');
INSERT INTO `build_auth_key` VALUES (60189,'Win','A64','WoW',_binary '\è\÷‰Ç\í‘L‡*\ïmsQ\íø');
INSERT INTO `build_auth_key` VALUES (60189,'Win','x64','WoW',_binary 'Ù£¬\Ã\á\"#\ÙQ\Ü?¸®\Ä');
INSERT INTO `build_auth_key` VALUES (60189,'Win','x64','WoWC',_binary '§ ¾-œ£ß½&p ­');
INSERT INTO `build_auth_key` VALUES (60228,'Mac','A64','WoW',_binary '±Lü\÷\Ù*\äü\ô¼\Ç\Òc');
INSERT INTO `build_auth_key` VALUES (60228,'Mac','A64','WoWC',_binary '\ó\Òj\Ìƒ£ül\ëIH\ÂwV');
INSERT INTO `build_auth_key` VALUES (60228,'Mac','x64','WoW',_binary '‹µQ‹\Ğıj]­%');
INSERT INTO `build_auth_key` VALUES (60228,'Mac','x64','WoWC',_binary 'Ÿ\á\È%•7\ö°ø¹¼\Ä#\ÒA');
INSERT INTO `build_auth_key` VALUES (60228,'Win','A64','WoW',_binary '\Ğ\\‘¨VN\ÜHê¿¶ù\Ğo');
INSERT INTO `build_auth_key` VALUES (60228,'Win','x64','WoW',_binary '[o\Ó\r\Ì7\Ñ\Ã\ÙË”¤N³N');
INSERT INTO `build_auth_key` VALUES (60228,'Win','x64','WoWC',_binary '\Î\Ä%Mı#Ñª\Ègb*');
INSERT INTO `build_auth_key` VALUES (60257,'Mac','A64','WoW',_binary 'µ»º\0—\Ò\Êu]ºq¥Š');
INSERT INTO `build_auth_key` VALUES (60257,'Mac','A64','WoWC',_binary '\ÜüD\ÂtR\ó·\ã>\æ\ğ');
INSERT INTO `build_auth_key` VALUES (60257,'Mac','x64','WoW',_binary ':\rµ\ì¾Ì¥\ÈpO¤\ÚS‰');
INSERT INTO `build_auth_key` VALUES (60257,'Mac','x64','WoWC',_binary 'MB\0Ø›\ñ:b\Ş\ßn&X\'');
INSERT INTO `build_auth_key` VALUES (60257,'Win','A64','WoW',_binary ']\â•W¥s\ñnÀ\Ò›%iS');
INSERT INTO `build_auth_key` VALUES (60257,'Win','x64','WoW',_binary '†3	¸†\Z«c‰<\ÒAI`');
INSERT INTO `build_auth_key` VALUES (60257,'Win','x64','WoWC',_binary 'm8o\ÚË«¨†¯®8&ş1ú');
INSERT INTO `build_auth_key` VALUES (60392,'Mac','A64','WoW',_binary ',¸®€;\ÙE}\Ö\ßa(');
INSERT INTO `build_auth_key` VALUES (60392,'Mac','A64','WoWC',_binary '\ßÎ‰Ó‹UŠ—\ãK‚ÓŒir\É');
INSERT INTO `build_auth_key` VALUES (60392,'Mac','x64','WoW',_binary 'İ?Bº$\ğc—\İ\0ú');
INSERT INTO `build_auth_key` VALUES (60392,'Mac','x64','WoWC',_binary '\Şj?)WÆŠN\nº\ë…G\á\'');
INSERT INTO `build_auth_key` VALUES (60392,'Win','A64','WoW',_binary '\ñşì¯†\àş\ôÖ¬ED');
INSERT INTO `build_auth_key` VALUES (60392,'Win','x64','WoW',_binary '\Ò&\"˜x®…\é‹\Úÿ\ëA');
INSERT INTO `build_auth_key` VALUES (60392,'Win','x64','WoWC',_binary '‰l\áº\Ül\Ñ\ÖQi­c€;\Õ');
INSERT INTO `build_auth_key` VALUES (60428,'Mac','A64','WoW',_binary '.}‚\Ûm*¶©\Õ#·sÁ\Ù!');
INSERT INTO `build_auth_key` VALUES (60428,'Mac','A64','WoWC',_binary '\r£Z†¯	…›\"ù\Ê\á(\ÍI');
INSERT INTO `build_auth_key` VALUES (60428,'Mac','x64','WoW',_binary 'ú\ôPS\0b	’\Íl\Ï');
INSERT INTO `build_auth_key` VALUES (60428,'Mac','x64','WoWC',_binary '!ƒƒEgQ8üU\0+N+¹_7');
INSERT INTO `build_auth_key` VALUES (60428,'Win','A64','WoW',_binary '2¬a™PB¦5[ı\Şb¼u');
INSERT INTO `build_auth_key` VALUES (60428,'Win','x64','WoW',_binary '&NZƒ\0\Ë?½Ñƒ¿\ô\ï­O');
INSERT INTO `build_auth_key` VALUES (60428,'Win','x64','WoWC',_binary '§(®\éœ\èÒ™;\n±v\Æh');
INSERT INTO `build_auth_key` VALUES (60490,'Mac','A64','WoW',_binary '¹(œF§XÏ»>O-¤\èŒJ');
INSERT INTO `build_auth_key` VALUES (60490,'Mac','A64','WoWC',_binary '\â\Ër+£	¹>Ğ¤_\Ş$]');
INSERT INTO `build_auth_key` VALUES (60490,'Mac','x64','WoW',_binary '\nGø@\Ìø\â\ó|ù9{µ²');
INSERT INTO `build_auth_key` VALUES (60490,'Mac','x64','WoWC',_binary '„UqF.\â%K\\W,‘ˆ\Ú');
INSERT INTO `build_auth_key` VALUES (60490,'Win','A64','WoW',_binary '½J‡Œÿ\ó&&i5X)I\ò\Í');
INSERT INTO `build_auth_key` VALUES (60490,'Win','x64','WoW',_binary '¾	/*\ê\0Í§\æ‡_6e');
INSERT INTO `build_auth_key` VALUES (60490,'Win','x64','WoWC',_binary '\"‚$aÁb`\Ñ$h‡_');
INSERT INTO `build_auth_key` VALUES (60568,'Mac','A64','WoW',_binary 'G\'I‡s7yn‹,®t\Û');
INSERT INTO `build_auth_key` VALUES (60568,'Mac','A64','WoWC',_binary 'L¶y4GV¤Ó¤\Ü\rz©');
INSERT INTO `build_auth_key` VALUES (60568,'Mac','x64','WoW',_binary '—Îƒaµµ\ğ\æ4c\Ã\ËŒES');
INSERT INTO `build_auth_key` VALUES (60568,'Mac','x64','WoWC',_binary '!^P¶\å`Ğ“s.§©[');
INSERT INTO `build_auth_key` VALUES (60568,'Win','A64','WoW',_binary 'x‹.CO”\Â\ó	d(\æ');
INSERT INTO `build_auth_key` VALUES (60568,'Win','x64','WoW',_binary '.¡Á1W\'\ò\Í\òX¢');
INSERT INTO `build_auth_key` VALUES (60568,'Win','x64','WoWC',_binary '-\r~ƒ»®8z\':À');
INSERT INTO `build_auth_key` VALUES (60822,'Mac','A64','WoW',_binary '¯Á†‡\â1‹¶ \ä—+Z=i¿');
INSERT INTO `build_auth_key` VALUES (60822,'Mac','A64','WoWC',_binary 'ŸnmO\î\Ğ\Ğû (BW..');
INSERT INTO `build_auth_key` VALUES (60822,'Mac','x64','WoW',_binary '\Ş!nJ¥†\Z•&\ä‘Lı†z');
INSERT INTO `build_auth_key` VALUES (60822,'Mac','x64','WoWC',_binary 'À|¼eÀn\Ü\Æ\Ù	¥v\Ú');
INSERT INTO `build_auth_key` VALUES (60822,'Win','A64','WoW',_binary 'V–\İI62\r‡hŠ0•\n');
INSERT INTO `build_auth_key` VALUES (60822,'Win','x64','WoW',_binary ':¬_·h\ß\É>«');
INSERT INTO `build_auth_key` VALUES (60822,'Win','x64','WoWC',_binary '_j{!\×p\ô4\ï”E\÷');
INSERT INTO `build_auth_key` VALUES (61122,'Mac','A64','WoW',_binary 'Ø˜ƒ\n\ïÀ\0\ä\Z\îª}\ĞKQ');
INSERT INTO `build_auth_key` VALUES (61122,'Mac','A64','WoWC',_binary 'X¯hn\Z7µP\Óu\Ö\Ë');
INSERT INTO `build_auth_key` VALUES (61122,'Mac','x64','WoW',_binary 'V®ÿüş¸’¢¨\ÄxŠ\ğ:×¼');
INSERT INTO `build_auth_key` VALUES (61122,'Win','A64','WoW',_binary 'š\Ä5yş‹\ä\çj %\Z/V<a');
INSERT INTO `build_auth_key` VALUES (61122,'Win','x64','WoW',_binary ']\æ³\àm8\àVm\Ö F™');
INSERT INTO `build_auth_key` VALUES (61122,'Win','x64','WoWC',_binary '„Ÿ\á\Z§\òXZF‘\ôQ');
INSERT INTO `build_auth_key` VALUES (61188,'Mac','A64','WoW',_binary '_ì¤º^\\şM–^”ÃŸ\îqŠ');
INSERT INTO `build_auth_key` VALUES (61188,'Mac','A64','WoWC',_binary '£\ò¸=z~5ª0ÿ\Ï\Ú\è\á');
INSERT INTO `build_auth_key` VALUES (61188,'Mac','x64','WoW',_binary '_§ı…¼\ç^†›\Û\èy’8');
INSERT INTO `build_auth_key` VALUES (61188,'Mac','x64','WoWC',_binary '\Ç\Ò\ÃÁ4\ÅtM½2\Åcµ¾œ‰');
INSERT INTO `build_auth_key` VALUES (61188,'Win','A64','WoW',_binary '™›\÷\Ô8“\ï$sU8‘U°\ç');
INSERT INTO `build_auth_key` VALUES (61188,'Win','x64','WoW',_binary 'ø¤.±F¤\İ\ÙO\Ó_ù­u\ß');
INSERT INTO `build_auth_key` VALUES (61188,'Win','x64','WoWC',_binary 'C˜:Md5“Ÿn\ç\ê\éU\ÖÈª');
INSERT INTO `build_auth_key` VALUES (61265,'Mac','A64','WoW',_binary 'Ş©b\Ú\î%R¤2®0u<=');
INSERT INTO `build_auth_key` VALUES (61265,'Mac','A64','WoWC',_binary ':\î\Ğ§³\Ä)\ğÔ„¸Y');
INSERT INTO `build_auth_key` VALUES (61265,'Mac','x64','WoW',_binary '\ßtÙƒŞ\Ñhƒ‚›\îI');
INSERT INTO `build_auth_key` VALUES (61265,'Mac','x64','WoWC',_binary '¦\ä iQ\r^°\á^!\ß');
INSERT INTO `build_auth_key` VALUES (61265,'Win','A64','WoW',_binary '‡\ĞS@\Ş\Å¹e“^ 5§');
INSERT INTO `build_auth_key` VALUES (61265,'Win','x64','WoW',_binary '\Ú\ÆU\ì\Ôy\\6\â§\ßc«~9');
INSERT INTO `build_auth_key` VALUES (61265,'Win','x64','WoWC',_binary 'Àa¥…†å°¡4¼S¸Ï¥');
INSERT INTO `build_auth_key` VALUES (61491,'Mac','A64','WoW',_binary 'jcù±B(¸Z\ô\îŸj­-Á');
INSERT INTO `build_auth_key` VALUES (61491,'Mac','A64','WoWC',_binary '&: \Ä{š\ò\Ş\ÌbÏ¼‡¸');
INSERT INTO `build_auth_key` VALUES (61491,'Mac','x64','WoW',_binary 'BuB\"û\ïK‡*³OZSg');
INSERT INTO `build_auth_key` VALUES (61491,'Mac','x64','WoWC',_binary '•\ŞL«4_\ã\'¢¹\Ä\Ê\ó');
INSERT INTO `build_auth_key` VALUES (61491,'Win','A64','WoW',_binary '¤.\æfÀOY\Î__dX\Ô');
INSERT INTO `build_auth_key` VALUES (61491,'Win','x64','WoW',_binary '\ì\ã+H3º\"<3\î\ÔÀ\Ñ');
INSERT INTO `build_auth_key` VALUES (61491,'Win','x64','WoWC',_binary 'Nl\'I\r“,y…\ñ’¬5\æ');
INSERT INTO `build_auth_key` VALUES (61559,'Mac','A64','WoW',_binary '<&\É\ÖgB\ô<\\Sd\Ï');
INSERT INTO `build_auth_key` VALUES (61559,'Mac','A64','WoWC',_binary '2\ö«‡\é\éwk\ê¬\è¶h:9º');
INSERT INTO `build_auth_key` VALUES (61559,'Mac','x64','WoW',_binary '\Òœ±\îàµ••\áE¸©b`');
INSERT INTO `build_auth_key` VALUES (61559,'Mac','x64','WoWC',_binary '\ç9x\ÉWh3\óf”)06\åT	');
INSERT INTO `build_auth_key` VALUES (61559,'Win','A64','WoW',_binary 'ªz\ÕbIy\ë6¦–:Ÿ}\à¹');
INSERT INTO `build_auth_key` VALUES (61559,'Win','x64','WoW',_binary '’Šs)\õ>\'8‘\ØZ—2HÀ');
INSERT INTO `build_auth_key` VALUES (61559,'Win','x64','WoWC',_binary '\Ù\år8\İB´Nh\ğ]Y¦#K');
INSERT INTO `build_auth_key` VALUES (61609,'Mac','A64','WoW',_binary ';_=-Ş²š»m´\ò8\É');
INSERT INTO `build_auth_key` VALUES (61609,'Mac','A64','WoWC',_binary 'YI\ó®+Q£°1U¡9R\è/w');
INSERT INTO `build_auth_key` VALUES (61609,'Mac','x64','WoW',_binary '|†e\î\Ó\ä\É\è²üIr\ôcl');
INSERT INTO `build_auth_key` VALUES (61609,'Mac','x64','WoWC',_binary '6û\Ë\Ğ\Ìzˆª¯›\Ó\Ïü');
INSERT INTO `build_auth_key` VALUES (61609,'Win','A64','WoW',_binary '!I?Œ\ÚfB®¨rCQ');
INSERT INTO `build_auth_key` VALUES (61609,'Win','x64','WoW',_binary 'ŸÀ,Å†Š’\\\İB¨\×ù\Ú');
INSERT INTO `build_auth_key` VALUES (61609,'Win','x64','WoWC',_binary '\ô\Êl!Q´´~Ã«e\ÆT');
INSERT INTO `build_auth_key` VALUES (61967,'Mac','A64','WoW',_binary 'Ú¶\÷x\Ãu1%Z\Â/?¹^ª');
INSERT INTO `build_auth_key` VALUES (61967,'Mac','A64','WoWC',_binary '?ÿiŸ“‹p|Š~\0');
INSERT INTO `build_auth_key` VALUES (61967,'Mac','x64','WoW',_binary '+´\ò·„˜q¨ù_\î\óW\é');
INSERT INTO `build_auth_key` VALUES (61967,'Mac','x64','WoWC',_binary 'M\ã\Ô@\Ñ\Ôj8\ì2\Ğ\Õ$\0Jº');
INSERT INTO `build_auth_key` VALUES (61967,'Win','A64','WoW',_binary '{D;¶´\óU¹\ã‰\ô\ğ¬°O\è');
INSERT INTO `build_auth_key` VALUES (61967,'Win','x64','WoW',_binary 'nKKSv\à•8zÿM');
INSERT INTO `build_auth_key` VALUES (61967,'Win','x64','WoWC',_binary '[\ğ´\İ!¯1Š\ër€');
INSERT INTO `build_auth_key` VALUES (62213,'Mac','A64','WoW',_binary 'N´y¡ …Ï¥–3\Û\Ê\Ú');
INSERT INTO `build_auth_key` VALUES (62213,'Mac','A64','WoWC',_binary '\"Å«C¢—bƒ\Ä\Ä`º');
INSERT INTO `build_auth_key` VALUES (62213,'Mac','x64','WoW',_binary '”Õ°©b3\Ä\ğh¥\÷ı^\Æ');
INSERT INTO `build_auth_key` VALUES (62213,'Mac','x64','WoWC',_binary 'º£ºüO’	¯¹\í¤È»Pl');
INSERT INTO `build_auth_key` VALUES (62213,'Win','A64','WoW',_binary '\õe\æ2\ËÀE\ò5\ò¤\Æi');
INSERT INTO `build_auth_key` VALUES (62213,'Win','x64','WoW',_binary '%”>©u>$v«N!¼');
INSERT INTO `build_auth_key` VALUES (62213,'Win','x64','WoWC',_binary '…„#~5qˆ\ÍS•x²');
INSERT INTO `build_auth_key` VALUES (62417,'Mac','A64','WoW',_binary '»¦,e90•®ü:');
INSERT INTO `build_auth_key` VALUES (62417,'Mac','A64','WoWC',_binary '\Ú{µç˜…jg´B\ëh=\Õ');
INSERT INTO `build_auth_key` VALUES (62417,'Mac','x64','WoW',_binary 'RYe¥ŸL\Zˆnk\Ì	\×Z');
INSERT INTO `build_auth_key` VALUES (62417,'Mac','x64','WoWC',_binary 'É²v±›ZY·ù\ã');
INSERT INTO `build_auth_key` VALUES (62417,'Win','A64','WoW',_binary '\Üt¯$•2\ï6p#Ç\æ\÷	');
INSERT INTO `build_auth_key` VALUES (62417,'Win','x64','WoW',_binary 'p/\Èmy&,\Ó9:\ë?[ˆ\æ');
INSERT INTO `build_auth_key` VALUES (62417,'Win','x64','WoWC',_binary '¹{%z=i»£!\ŞÁBu‘\ê\Ö');
INSERT INTO `build_auth_key` VALUES (62438,'Mac','A64','WoW',_binary '\\—MoH\÷z`\Ñ%+');
INSERT INTO `build_auth_key` VALUES (62438,'Mac','A64','WoWC',_binary '3>\ï]	…”\Ğ“ûUf\Ä');
INSERT INTO `build_auth_key` VALUES (62438,'Mac','x64','WoW',_binary '\è\é\ó\'{™\\?·j”r1m');
INSERT INTO `build_auth_key` VALUES (62438,'Mac','x64','WoWC',_binary '-’ú\É{\á`<\÷\Ä&\ã');
INSERT INTO `build_auth_key` VALUES (62438,'Win','A64','WoW',_binary '_C¹\Ç}¦1ª\õ\å\'ük º');
INSERT INTO `build_auth_key` VALUES (62438,'Win','x64','WoW',_binary 'T¸\ny¸¥\Å\âSf«W\ğ¬°');
INSERT INTO `build_auth_key` VALUES (62438,'Win','x64','WoWC',_binary '\Ê\ä\Z\×Ïœ.…ãˆ»\ïm');
INSERT INTO `build_auth_key` VALUES (62493,'Mac','A64','WoW',_binary '5f>\õ8%~—hH”\Ü');
INSERT INTO `build_auth_key` VALUES (62493,'Mac','A64','WoWC',_binary 'ZHÓ¤2x¦\ê1²\ã®\r\æ\æ');
INSERT INTO `build_auth_key` VALUES (62493,'Mac','x64','WoW',_binary '\î\Ä\'\É\ô„\ïªÀH\ÕR');
INSERT INTO `build_auth_key` VALUES (62493,'Mac','x64','WoWC',_binary '°¡\×\ê5X‘cEÍ„.');
INSERT INTO `build_auth_key` VALUES (62493,'Win','A64','WoW',_binary '0%\ÙEgy/¨(L(.w\å');
INSERT INTO `build_auth_key` VALUES (62493,'Win','x64','WoW',_binary '[pz¶:†,.¨Hr ');
INSERT INTO `build_auth_key` VALUES (62493,'Win','x64','WoWC',_binary '\Ze¹4\ÔN\ğ§ty=	‚&');
INSERT INTO `build_auth_key` VALUES (62680,'Mac','A64','WoW',_binary 'R\Ó\Ïø®rM\ñµ(Š‹');
INSERT INTO `build_auth_key` VALUES (62680,'Mac','A64','WoWC',_binary '»\ò7~ƒ¹€²P²øŒ©');
INSERT INTO `build_auth_key` VALUES (62680,'Mac','x64','WoW',_binary '\è\ä\Ù\Å8Úµ\ö#\ë\ó\÷¥¨');
INSERT INTO `build_auth_key` VALUES (62680,'Mac','x64','WoWC',_binary '¬¯O.\'\Ï\Í\0x{±™ù/,');
INSERT INTO `build_auth_key` VALUES (62680,'Win','A64','WoW',_binary 'û‡ŸKe7A#\ÏY¦PˆZX');
INSERT INTO `build_auth_key` VALUES (62680,'Win','x64','WoW',_binary '.M\Ş\Ş#<†¾yg\Z…J‰');
INSERT INTO `build_auth_key` VALUES (62680,'Win','x64','WoWC',_binary 'S–±\ÙVc»·«\0;¶Ğº^');
INSERT INTO `build_auth_key` VALUES (62706,'Mac','A64','WoW',_binary 'srÒŒfoxEr\İhL{\ã4');
INSERT INTO `build_auth_key` VALUES (62706,'Mac','A64','WoWC',_binary '\0g\õ{Û‘o$†\Û6');
INSERT INTO `build_auth_key` VALUES (62706,'Mac','x64','WoW',_binary 'ÃLK\âU\áW‹È£{F/');
INSERT INTO `build_auth_key` VALUES (62706,'Mac','x64','WoWC',_binary '¼ú\ä!ş\Ôt‹®•\ég\î\í');
INSERT INTO `build_auth_key` VALUES (62706,'Win','A64','WoW',_binary '0¬İ€¡\ëWÆ§\ò\ä/\0ü}#');
INSERT INTO `build_auth_key` VALUES (62706,'Win','x64','WoW',_binary '\â\Ôf4hK‘7dY\Ûrb„\ñ\â');
INSERT INTO `build_auth_key` VALUES (62706,'Win','x64','WoWC',_binary '°œ§\Ô\ìr,=¥@­AF');
INSERT INTO `build_auth_key` VALUES (62748,'Mac','A64','WoW',_binary 'œ¬û\ãı¨=¬SB‚!');
INSERT INTO `build_auth_key` VALUES (62748,'Mac','A64','WoWC',_binary '‡§C_\ç\ïfû}¥`‰\Ğq');
INSERT INTO `build_auth_key` VALUES (62748,'Mac','x64','WoW',_binary 'ƒ¦Fw4\ë/%\áš!g1');
INSERT INTO `build_auth_key` VALUES (62748,'Mac','x64','WoWC',_binary '¹¦t(¹¦X\ìZ\êJ<ú\Ã');
INSERT INTO `build_auth_key` VALUES (62748,'Win','A64','WoW',_binary 'VÅ1\ç\Ót…\Ú\ÒZ+Ä¤›†');
INSERT INTO `build_auth_key` VALUES (62748,'Win','x64','WoW',_binary '	Ÿ\æ\Ü:Ğu\r¨\Ğ2´');
INSERT INTO `build_auth_key` VALUES (62748,'Win','x64','WoWC',_binary 'n\Ê3(RÙ¿\÷|\â©\×\Z—mU');
INSERT INTO `build_auth_key` VALUES (62801,'Mac','A64','WoW',_binary 'G\'\0°ù¤\ê\ÃKG·(');
INSERT INTO `build_auth_key` VALUES (62801,'Mac','A64','WoWC',_binary '\ò1º‹uã¸¢.K`#A¿b');
INSERT INTO `build_auth_key` VALUES (62801,'Mac','x64','WoWC',_binary '‚m,\\M)p‡5…\İ\Ät=');
INSERT INTO `build_auth_key` VALUES (62801,'Win','A64','WoW',_binary '\ß\È4\r³Yiä·“\ï)Š:\â·');
INSERT INTO `build_auth_key` VALUES (62801,'Win','x64','WoW',_binary 'Œo«\òš¼YŸ¯¡\ã\Ëw');
INSERT INTO `build_auth_key` VALUES (62801,'Win','x64','WoWC',_binary 'UÄ•\ã2|\'§\å\Z\Ú\îB');
INSERT INTO `build_auth_key` VALUES (62876,'Mac','A64','WoW',_binary '“\Ùz\çü\õ\Õ5<\Ãhˆ+¦«');
INSERT INTO `build_auth_key` VALUES (62876,'Mac','A64','WoWC',_binary '~®¤_\rùXxªKl\n;');
INSERT INTO `build_auth_key` VALUES (62876,'Mac','x64','WoW',_binary 'É²,IşE\ÑèŸ…\ã\ñGN');
INSERT INTO `build_auth_key` VALUES (62876,'Mac','x64','WoWC',_binary '4\èµ\òsSXçº•\Òƒ');
INSERT INTO `build_auth_key` VALUES (62876,'Win','A64','WoW',_binary ' ¾ƒ\â|±\İ\æ\Ê\éŞ­\í\Ò\Î');
INSERT INTO `build_auth_key` VALUES (62876,'Win','x64','WoW',_binary 'Gz\İÔƒ‹|\ô\ã\È$7º');
INSERT INTO `build_auth_key` VALUES (62876,'Win','x64','WoWC',_binary '\Í6Ú«\õ\ï\õKG¼%\ô@ŠT');
INSERT INTO `build_auth_key` VALUES (62958,'Mac','A64','WoW',_binary '\îwÖ¿¤[«\õ?\ä\ÇrWE™¦');
INSERT INTO `build_auth_key` VALUES (62958,'Mac','A64','WoWC',_binary '}8`ù°\é\å9’B\ô\ê\ì†\r');
INSERT INTO `build_auth_key` VALUES (62958,'Mac','x64','WoW',_binary 'cû\ì§\ÑYiT’‡9,|±š');
INSERT INTO `build_auth_key` VALUES (62958,'Mac','x64','WoWC',_binary '21l‹:\ôı\Ïzgıwi§\ñ+');
INSERT INTO `build_auth_key` VALUES (62958,'Win','A64','WoW',_binary 'd•¤:JÚŠH');
INSERT INTO `build_auth_key` VALUES (62958,'Win','x64','WoW',_binary '\×/‘;U\É,XEÏ¶‡x?');
INSERT INTO `build_auth_key` VALUES (62958,'Win','x64','WoWC',_binary 'nvz\×Y¢±œ(‘\'¤Ô®­');
INSERT INTO `build_auth_key` VALUES (63003,'Mac','A64','WoW',_binary '\Ã\ß\ñù\0·M\Ñ\Æ\0Y\×>o');
INSERT INTO `build_auth_key` VALUES (63003,'Mac','A64','WoWC',_binary ';¬\ÑÀ¦I›n›•ƒ;Ÿ');
INSERT INTO `build_auth_key` VALUES (63003,'Mac','x64','WoW',_binary '|\à!\ã\Ø\ÒS\÷#\Z¯M\ìJ');
INSERT INTO `build_auth_key` VALUES (63003,'Mac','x64','WoWC',_binary 'Qt{Òškc]ˆ\Ø\êR\ö\ÓA');
INSERT INTO `build_auth_key` VALUES (63003,'Win','A64','WoW',_binary '\0\ò@=\Ø\íù\Ó\Ş\õ¯Q¦\Ü');
INSERT INTO `build_auth_key` VALUES (63003,'Win','x64','WoW',_binary 'm5Mû8…V\×¸˜\Â');
INSERT INTO `build_auth_key` VALUES (63003,'Win','x64','WoWC',_binary ',u\Ô\÷_B0¥qn™Z');
INSERT INTO `build_auth_key` VALUES (63163,'Mac','A64','WoW',_binary '¨\Úøx?¼\í\È8\ğmù3~´');
INSERT INTO `build_auth_key` VALUES (63163,'Mac','A64','WoWC',_binary '¤G\Ú8\ñ)™\Ælo@<\ë!');
INSERT INTO `build_auth_key` VALUES (63163,'Mac','x64','WoW',_binary '9jù\r•\ÌO\r\ò¥Œ;\ã\ÚlU');
INSERT INTO `build_auth_key` VALUES (63163,'Mac','x64','WoWC',_binary '\rsÁ\'«\Ì9Ë¹‰vø|\rI');
INSERT INTO `build_auth_key` VALUES (63163,'Win','A64','WoW',_binary '\ØauJø¿ù­U´\rO');
INSERT INTO `build_auth_key` VALUES (63163,'Win','x64','WoW',_binary 'y&vü8\ë\è¾À]\äx¸\ß');
INSERT INTO `build_auth_key` VALUES (63163,'Win','x64','WoWC',_binary '\è1\Ï‰)\ì™*S‰‚+\à');
INSERT INTO `build_auth_key` VALUES (63305,'Mac','A64','WoW',_binary '\"\çÀZ\êı¬ÿÂ®K\'\Ş');
INSERT INTO `build_auth_key` VALUES (63305,'Mac','A64','WoWC',_binary '¾°\ò[!\ô>\õ*\ğú¥r\æ\è');
INSERT INTO `build_auth_key` VALUES (63305,'Mac','x64','WoW',_binary 'şm5\Ù\Z#±Œ>\É\ÏX|²');
INSERT INTO `build_auth_key` VALUES (63305,'Mac','x64','WoWC',_binary '\r`\n—\Ç x(\ò\ŞÀÜ¤›À¨');
INSERT INTO `build_auth_key` VALUES (63305,'Win','A64','WoW',_binary 'ºf\ï\Õ\"\ë,Ü?ùKÍ‘\Ê');
INSERT INTO `build_auth_key` VALUES (63305,'Win','x64','WoW',_binary '“\Ä;ji\Ê7\ñ\\hª¸\æ“!');
INSERT INTO `build_auth_key` VALUES (63305,'Win','x64','WoWC',_binary 'G»¸Ò¬M \×O\ïÀ\İ2”');
INSERT INTO `build_auth_key` VALUES (63506,'Mac','A64','WoW',_binary '\ŞRœA\Êd\È9\í&¿N');
INSERT INTO `build_auth_key` VALUES (63506,'Mac','A64','WoWC',_binary 'w¬‡‘Î¢\Û_‡!Z-`');
INSERT INTO `build_auth_key` VALUES (63506,'Mac','x64','WoW',_binary '?#\ëâ¨ \Â\È0\å,›Õ³');
INSERT INTO `build_auth_key` VALUES (63506,'Mac','x64','WoWC',_binary ' Ò p·‹ı\îs‚|€\Î\Û\Ü');
INSERT INTO `build_auth_key` VALUES (63506,'Win','A64','WoW',_binary 'm]\Æ8¿J\æ½\ÃJ¸bJ\êI§');
INSERT INTO `build_auth_key` VALUES (63506,'Win','x64','WoW',_binary 'hc;\ÎZ¤ƒœF¹\Ç\ò1');
INSERT INTO `build_auth_key` VALUES (63506,'Win','x64','WoWC',_binary '-=™:\õO\Ì„cø€úÿ');
INSERT INTO `build_auth_key` VALUES (63660,'Mac','A64','WoW',_binary '¥ı`¶ş`\r\ÍA\Ú\Ô\ßü\ó\Ê');
INSERT INTO `build_auth_key` VALUES (63660,'Mac','A64','WoWC',_binary '\Ã:UÿÁ«Œ\\¶\Ò\É_M0');
INSERT INTO `build_auth_key` VALUES (63660,'Mac','x64','WoW',_binary '[¨ü ˜»½Ÿ³x¹[am¦');
INSERT INTO `build_auth_key` VALUES (63660,'Mac','x64','WoWC',_binary 'Rº\nk$2\×XOYzı8À…');
INSERT INTO `build_auth_key` VALUES (63660,'Win','A64','WoW',_binary 'X\Í\ôÚ¯¡!\Zû\Ä\öhµ');
INSERT INTO `build_auth_key` VALUES (63660,'Win','x64','WoW',_binary '\Ö\ß´¾—\éT½L\r\Ì<');
INSERT INTO `build_auth_key` VALUES (63660,'Win','x64','WoWC',_binary 'vª\õŠi‰\'\Ù\"mê«­\Ã');
INSERT INTO `build_auth_key` VALUES (63704,'Mac','A64','WoW',_binary 'ˆ¦\çú™\Ö\èBÿ´\ÛjP] ');
INSERT INTO `build_auth_key` VALUES (63704,'Mac','A64','WoWC',_binary '\ô\ö|Ú9©jc0ı[3\Z');
INSERT INTO `build_auth_key` VALUES (63704,'Mac','x64','WoW',_binary 'lÍ¯\ÒL¿\ÕÄ–\n—ù');
INSERT INTO `build_auth_key` VALUES (63704,'Mac','x64','WoWC',_binary '\öº	>\à›x\Ü^pÿ§\Ê\äœ');
INSERT INTO `build_auth_key` VALUES (63704,'Win','A64','WoW',_binary '…[œı…©F¯®\ï,\÷¹ˆ\Ì');
INSERT INTO `build_auth_key` VALUES (63704,'Win','x64','WoW',_binary '|´ƒ\ë\Õ)p\ĞË»yRl\êú');
INSERT INTO `build_auth_key` VALUES (63704,'Win','x64','WoWC',_binary 'I…\ë\Õşx\ë\ÇÔ\ïny');
INSERT INTO `build_auth_key` VALUES (63796,'Mac','A64','WoW',_binary '7\Æ\Ör\áÆ€YK7M\â•uŸ');
INSERT INTO `build_auth_key` VALUES (63796,'Mac','A64','WoWC',_binary '_ˆ-U%zU¦‚…\ä');
INSERT INTO `build_auth_key` VALUES (63796,'Mac','x64','WoW',_binary '¡T¶Õ˜\Z«\áĞ‰\ív`b');
INSERT INTO `build_auth_key` VALUES (63796,'Mac','x64','WoWC',_binary '˜t–V¥\èDı\è\èŸ\ÜM¶');
INSERT INTO `build_auth_key` VALUES (63796,'Win','A64','WoW',_binary '\ñ±UŒ\Ş\ğd\è\Ğ\×\Ğ”c');
INSERT INTO `build_auth_key` VALUES (63796,'Win','x64','WoW',_binary '-O¶Z+ªBˆW/ŒJTh');
INSERT INTO `build_auth_key` VALUES (63796,'Win','x64','WoWC',_binary 'C––®=(\ç@\à?A');
INSERT INTO `build_auth_key` VALUES (63834,'Mac','A64','WoW',_binary 'û\ÛKı˜ú\ò,Œû‘Ô°\ÏV');
INSERT INTO `build_auth_key` VALUES (63834,'Mac','A64','WoWC',_binary 'z\Â Ë¼$£T~D­');
INSERT INTO `build_auth_key` VALUES (63834,'Mac','x64','WoW',_binary '\Ñ\äË‰lTœ.@C\ÎYo');
INSERT INTO `build_auth_key` VALUES (63834,'Mac','x64','WoWC',_binary 'Á\Ñm(“ u)\õK\r”x');
INSERT INTO `build_auth_key` VALUES (63834,'Win','A64','WoW',_binary 'š¿3b@¨\ÕØ¥8\Ê\0˜	');
INSERT INTO `build_auth_key` VALUES (63834,'Win','x64','WoW',_binary '§\ç9\ğı\÷E¨yy\ì\èk\ò');
INSERT INTO `build_auth_key` VALUES (63834,'Win','x64','WoWC',_binary 'g€Š¡çŠ¯\İ4Pp\ô«@\İ\\');
INSERT INTO `build_auth_key` VALUES (63906,'Mac','A64','WoW',_binary '$˜·@“&›Â¥—¿6‹ø');
INSERT INTO `build_auth_key` VALUES (63906,'Mac','A64','WoWC',_binary '\Zkr\ÚPÎ„¿B\ò•-');
INSERT INTO `build_auth_key` VALUES (63906,'Mac','x64','WoW',_binary '<Ö¶8\ŞÔ€\ö¸‹œR¹');
INSERT INTO `build_auth_key` VALUES (63906,'Mac','x64','WoWC',_binary 'l“\÷xº\Ğp~¥qW\Ô');
INSERT INTO `build_auth_key` VALUES (63906,'Win','A64','WoW',_binary '^Ê¶Íbh\ğ+:Ì˜JWi');
INSERT INTO `build_auth_key` VALUES (63906,'Win','x64','WoW',_binary '¥\Ú\Âe\ÕgÁ\ñ(3‘&\Æ');
INSERT INTO `build_auth_key` VALUES (63906,'Win','x64','WoWC',_binary '\Ë,nP‚\ÂRŸl\Ò\æ\æ\Ù');
INSERT INTO `build_auth_key` VALUES (64154,'Mac','A64','WoW',_binary '+Gh{—ûQ\ï\Ì(\'K\ğ”\ï{');
INSERT INTO `build_auth_key` VALUES (64154,'Mac','A64','WoWC',_binary 'go;\\\Ù\'™µ\În\ËıBë¼–');
INSERT INTO `build_auth_key` VALUES (64154,'Mac','x64','WoW',_binary 'ÓŸ;\Í4\Ü,İ·>©\Ú\0');
INSERT INTO `build_auth_key` VALUES (64154,'Mac','x64','WoWC',_binary 'Ag/\Ò\0‚Ÿ+9\é¦7İ½');
INSERT INTO `build_auth_key` VALUES (64154,'Win','A64','WoW',_binary '½\æa¯ıR\nSia ˜‘:');
INSERT INTO `build_auth_key` VALUES (64154,'Win','x64','WoW',_binary ' ]qv*,Qj\\\ãk5\Â');
INSERT INTO `build_auth_key` VALUES (64154,'Win','x64','WoWC',_binary '8\÷„I^\ĞŸ\ó‘²~\ÃEH\à');
INSERT INTO `build_auth_key` VALUES (64270,'Mac','A64','WoW',_binary '’\0!‰ù‹\År|e\0C\\');
INSERT INTO `build_auth_key` VALUES (64270,'Mac','A64','WoWC',_binary '\ê&!H1³\n\Ë\ÂQ*\à\ç');
INSERT INTO `build_auth_key` VALUES (64270,'Mac','x64','WoW',_binary 'm`«/#E<#fD=t‚F0\È');
INSERT INTO `build_auth_key` VALUES (64270,'Mac','x64','WoWC',_binary 'Š« ¼t\ö(X\Ï4“cu‰');
INSERT INTO `build_auth_key` VALUES (64270,'Win','A64','WoW',_binary '\èİ“ kë‡…€¼(‚&‘');
INSERT INTO `build_auth_key` VALUES (64270,'Win','x64','WoW',_binary '`\ë\í¶„#‰>L1~´I¡·');
INSERT INTO `build_auth_key` VALUES (64270,'Win','x64','WoWC',_binary 'Em:\ö\Ç\ì™g³m\ô9\Å[\Ğ\î');
INSERT INTO `build_auth_key` VALUES (64395,'Mac','A64','WoW',_binary 'CYŞ€\Åc\ö’vŠ5·œ\Õ\0V');
INSERT INTO `build_auth_key` VALUES (64395,'Mac','A64','WoWC',_binary 'nGÿ\ÌYÀ—ıª\ê\â¥[\Ù_');
INSERT INTO `build_auth_key` VALUES (64395,'Mac','x64','WoW',_binary '-a+„å‘˜ı\ëHP\Øy');
INSERT INTO `build_auth_key` VALUES (64395,'Mac','x64','WoWC',_binary '¹\î\r\í6Z\r\Z\ÜÇ€Ï‘p6');
INSERT INTO `build_auth_key` VALUES (64395,'Win','A64','WoW',_binary '0„µ¨½•m	\ÇK\ál');
INSERT INTO `build_auth_key` VALUES (64395,'Win','x64','WoW',_binary '’|6,+•P\Ö? <ˆ»´½');
INSERT INTO `build_auth_key` VALUES (64395,'Win','x64','WoWC',_binary '°›•\Ñ@y\àK“{\r\í,G');
INSERT INTO `build_auth_key` VALUES (64484,'Mac','A64','WoW',_binary ' \Ö\ÂL@ı¦*K3\ä\âS6');
INSERT INTO `build_auth_key` VALUES (64484,'Mac','A64','WoWC',_binary '\ÒÒ–G\ŞQ\Î\ó\\\Öq¿”');
INSERT INTO `build_auth_key` VALUES (64484,'Mac','x64','WoW',_binary ';-+mœ\à•s\ä\Ø\æI±°');
INSERT INTO `build_auth_key` VALUES (64484,'Mac','x64','WoWC',_binary '„<¹şš?, \Ş\÷Ï­');
INSERT INTO `build_auth_key` VALUES (64484,'Win','A64','WoW',_binary '\r{€n\ğ\Õd$†mˆBY	’');
INSERT INTO `build_auth_key` VALUES (64484,'Win','x64','WoW',_binary '-\Ö\×X;¹v¯…µr\Ä\ÌA');
INSERT INTO `build_auth_key` VALUES (64484,'Win','x64','WoWC',_binary '!\ò0y\Õ\êeO!¸“\İ\Z4');
INSERT INTO `build_auth_key` VALUES (64502,'Mac','A64','WoW',_binary 'Ÿx,)X\ì\ó>x³´[');
INSERT INTO `build_auth_key` VALUES (64502,'Mac','A64','WoWC',_binary 'Y\â~\ÎAª\×wtf');
INSERT INTO `build_auth_key` VALUES (64502,'Mac','x64','WoW',_binary '\Úy_\ó–\r\è!*\Ö\É|F¿\Ğ');
INSERT INTO `build_auth_key` VALUES (64502,'Mac','x64','WoWC',_binary 'm\Î;©À¬\ö8®Y–€ ƒ®');
INSERT INTO `build_auth_key` VALUES (64502,'Win','A64','WoW',_binary 'Q\ß	«\ã\r8.µ”.\×t0');
INSERT INTO `build_auth_key` VALUES (64502,'Win','x64','WoW',_binary 'T\Ó\Ï+\ÈNP\éL-:•\Ím{');
INSERT INTO `build_auth_key` VALUES (64502,'Win','x64','WoWC',_binary '©hÏ¨°UC;>H>„O¿\Ã');
INSERT INTO `build_auth_key` VALUES (64877,'Mac','A64','WoW',_binary '¹GC\ë\Ü[«\öû`J:^i');
INSERT INTO `build_auth_key` VALUES (64877,'Mac','A64','WoWC',_binary '=ş\Ü%\é½\ò\È\Ã?n¶\Ô');
INSERT INTO `build_auth_key` VALUES (64877,'Mac','x64','WoW',_binary '˜FM\ßüšW†\Ú>¥ º2');
INSERT INTO `build_auth_key` VALUES (64877,'Mac','x64','WoWC',_binary '\0?Fz‹+\ßeu€ ˜');
INSERT INTO `build_auth_key` VALUES (64877,'Win','A64','WoW',_binary 'n\Ğp	™{…{³}Ëµ±w');
INSERT INTO `build_auth_key` VALUES (64877,'Win','x64','WoW',_binary 'j·È˜c©\ÖÍ©Àú\çş±&');
INSERT INTO `build_auth_key` VALUES (64877,'Win','x64','WoWC',_binary '½R\ğ\Ó\÷\É&B^–<\êBK\Ó');
INSERT INTO `build_auth_key` VALUES (64978,'Mac','A64','WoW',_binary '-=šYP¬¿£„P\"R\à');
INSERT INTO `build_auth_key` VALUES (64978,'Mac','A64','WoWC',_binary '¤j¦-\ìr?±l\âdB');
INSERT INTO `build_auth_key` VALUES (64978,'Mac','x64','WoW',_binary 'ŒM£et\Æ\æ‡\ÛE\\;sı');
INSERT INTO `build_auth_key` VALUES (64978,'Mac','x64','WoWC',_binary '\Ñ\îo\0P|\Çq\ôª\Øk‘\÷ª');
INSERT INTO `build_auth_key` VALUES (64978,'Win','A64','WoW',_binary 'ût¢‹\Z1×¢£mÒ·\Z·');
INSERT INTO `build_auth_key` VALUES (64978,'Win','x64','WoW',_binary 'pk¥z\íÙkZX Ÿ¦£·(');
INSERT INTO `build_auth_key` VALUES (64978,'Win','x64','WoWC',_binary '–´\Ô\Ñ\ñ,ø°\ÈU¢<gL,');
INSERT INTO `build_auth_key` VALUES (65299,'Mac','A64','WoW',_binary '\Ö|‹1\æ\àC\İTnk`‰\Ã');
INSERT INTO `build_auth_key` VALUES (65299,'Mac','A64','WoWC',_binary '’[\õf*ì„Q\ğw+‘');
INSERT INTO `build_auth_key` VALUES (65299,'Mac','x64','WoW',_binary '\\Q:1L\Ò\é\å’k²c\ÄY');
INSERT INTO `build_auth_key` VALUES (65299,'Mac','x64','WoWC',_binary '«LĞŒeoş	\Z\ß\Ôp¼\à\Õù');
INSERT INTO `build_auth_key` VALUES (65299,'Win','A64','WoW',_binary 'Ğ€.ÿSj.}¿\à¥U\êG');
INSERT INTO `build_auth_key` VALUES (65299,'Win','x64','WoW',_binary '’%˜û¸¹/iEİ®K^');
INSERT INTO `build_auth_key` VALUES (65299,'Win','x64','WoWC',_binary '¶µŠ”¤»Vr{.\Øe');
INSERT INTO `build_auth_key` VALUES (65560,'Mac','A64','WoW',_binary 'vJ\í\Í=˜´°\ä€	R\ó[!');
INSERT INTO `build_auth_key` VALUES (65560,'Mac','A64','WoWC',_binary 'lQB­\0\ï	i\ÎÅ”2');
INSERT INTO `build_auth_key` VALUES (65560,'Mac','x64','WoW',_binary '‚w3›\àÎøN\öšˆ©˜mP');
INSERT INTO `build_auth_key` VALUES (65560,'Mac','x64','WoWC',_binary 'oŠ˜Ôg•X:!R\Ãj{›');
INSERT INTO `build_auth_key` VALUES (65560,'Win','A64','WoW',_binary '\Ôfpªa4NB¤Ø®');
INSERT INTO `build_auth_key` VALUES (65560,'Win','x64','WoW',_binary 'ª›v/\Ë\İ\ö;6~Š@G\ã');
INSERT INTO `build_auth_key` VALUES (65560,'Win','x64','WoWC',_binary 'C~\á\á{˜nU¸(`Ÿ\Ñw=');
INSERT INTO `build_auth_key` VALUES (65655,'Mac','A64','WoW',_binary '_\É\"e}Á$j­\ó½0À\É');
INSERT INTO `build_auth_key` VALUES (65655,'Mac','A64','WoWC',_binary '©¬\î™ı\ß\"lü,3@\Ûe');
INSERT INTO `build_auth_key` VALUES (65655,'Mac','x64','WoW',_binary '\É\ëBO#\Æ:CÁ£v!i½·');
INSERT INTO `build_auth_key` VALUES (65655,'Mac','x64','WoWC',_binary 'h/“}`T\Çov?s');
INSERT INTO `build_auth_key` VALUES (65655,'Win','A64','WoW',_binary 'r\ÕP\Ãw´WÀmm€¬\Zşl');
INSERT INTO `build_auth_key` VALUES (65655,'Win','x64','WoW',_binary 'jÔ›i\ôú\ÓV‚4HÑ‰`');
INSERT INTO `build_auth_key` VALUES (65655,'Win','x64','WoWC',_binary '¸>v)w\Ú¤ıN¹Sœ');
INSERT INTO `build_auth_key` VALUES (65699,'Mac','A64','WoW',_binary '™\ğ¥g„|ªJÀ\ŞÊ¦[_');
INSERT INTO `build_auth_key` VALUES (65699,'Mac','A64','WoWC',_binary '\à\n>‘+J\ğ*ŠsÀ˜\È\ğ«j');
INSERT INTO `build_auth_key` VALUES (65699,'Mac','x64','WoW',_binary '¶\ÎÁ	k\à\ä~“’j1=³');
INSERT INTO `build_auth_key` VALUES (65699,'Mac','x64','WoWC',_binary 'Š‰©f|h?…\â\ß,£-À');
INSERT INTO `build_auth_key` VALUES (65699,'Win','A64','WoW',_binary '\ï“2Z\×pH®\ĞFGv)ÁCÁ');
INSERT INTO `build_auth_key` VALUES (65699,'Win','x64','WoW',_binary '½ŠaS¼û¯P\Ù?\Ãù‹');
INSERT INTO `build_auth_key` VALUES (65699,'Win','x64','WoWC',_binary 'O}+E\Äœ\ñ\r\ÕV\Ù£');
INSERT INTO `build_auth_key` VALUES (65727,'Mac','A64','WoW',_binary 'ß¼ä¤§¦\ä‚k\Çe\Ï\Ò\Ã');
INSERT INTO `build_auth_key` VALUES (65727,'Mac','A64','WoWC',_binary 'µ’\è\ÌcoŒ	B\'<½ˆ\Ó');
INSERT INTO `build_auth_key` VALUES (65727,'Mac','x64','WoW',_binary '¢\İ#\óg9Z\ÛB6W\é¯');
INSERT INTO `build_auth_key` VALUES (65727,'Mac','x64','WoWC',_binary 'k7ø\ó\Øw6úœ‚\0š\Íû');
INSERT INTO `build_auth_key` VALUES (65727,'Win','A64','WoW',_binary '\ï«^ªÅƒ\ËøÕ±\÷?\ëp');
INSERT INTO `build_auth_key` VALUES (65727,'Win','x64','WoW',_binary '\Â\Øş*¸šUz\0µ;²\áR');
INSERT INTO `build_auth_key` VALUES (65727,'Win','x64','WoWC',_binary '\Â[\Î\'\ÎM\Æ#ú\Æ{²«\Ï*');
INSERT INTO `build_auth_key` VALUES (65818,'Mac','A64','WoW',_binary '\õ\õıµSU¸F\×_\Öb…\æ');
INSERT INTO `build_auth_key` VALUES (65818,'Mac','A64','WoWC',_binary '|\"\ĞC–\"‚\å®y\ôr\İ§');
INSERT INTO `build_auth_key` VALUES (65818,'Mac','x64','WoW',_binary '\É\òT0š°-M\âÆŸ@lª\İm');
INSERT INTO `build_auth_key` VALUES (65818,'Mac','x64','WoWC',_binary '\ÑO£SD•=›Wlwk\Æ\Ò$¥');
INSERT INTO `build_auth_key` VALUES (65818,'Win','A64','WoW',_binary '\Ù4\æùüŠ»¾\ñË«¹M\æ');
INSERT INTO `build_auth_key` VALUES (65818,'Win','x64','WoW',_binary 'ù]¢b¤\ÔZB\"­ €l\Ö');
INSERT INTO `build_auth_key` VALUES (65818,'Win','x64','WoWC',_binary '1zen\\·:M8F');
INSERT INTO `build_auth_key` VALUES (65848,'Mac','A64','WoW',_binary '`E\ÊÕ¥\âÀ[¯Hš)µ‰\İ');
INSERT INTO `build_auth_key` VALUES (65848,'Mac','A64','WoWC',_binary '!\ær	O.Ù¶\éIK\ãN\í');
INSERT INTO `build_auth_key` VALUES (65848,'Mac','x64','WoW',_binary '9Ñ©\Ø\Î\Ş54a:X«D');
INSERT INTO `build_auth_key` VALUES (65848,'Mac','x64','WoWC',_binary '^äœª§¶jƒ\Â\Õˆ');
INSERT INTO `build_auth_key` VALUES (65848,'Win','A64','WoW',_binary 't¿rk\ë[¢.…_\rÆ«r²,');
INSERT INTO `build_auth_key` VALUES (65848,'Win','x64','WoW',_binary 'ü‘\Õ\ëx ¥7\æhù.9\r');
INSERT INTO `build_auth_key` VALUES (65848,'Win','x64','WoWC',_binary '&8„º^U6ıPYo½Á2');
INSERT INTO `build_auth_key` VALUES (65867,'Mac','A64','WoW',_binary '	úbœ*š*”\ÙXJ¥E®');
INSERT INTO `build_auth_key` VALUES (65867,'Mac','A64','WoWC',_binary '\r\Ñ\rº*Ïˆ\Ğf\r”,x');
INSERT INTO `build_auth_key` VALUES (65867,'Mac','x64','WoW',_binary '-M™¦:¢\Í|gÓ£r\Í\÷');
INSERT INTO `build_auth_key` VALUES (65867,'Mac','x64','WoWC',_binary '{x!ıA«J¼ùv!f‘g\Õ4');
INSERT INTO `build_auth_key` VALUES (65867,'Win','A64','WoW',_binary '\0\È\Û\0p,*­¯… \ÔX\Z\Ê\ä');
INSERT INTO `build_auth_key` VALUES (65867,'Win','x64','WoW',_binary '-¦+±Ph\Í$\×Wƒ\Ú{¤');
INSERT INTO `build_auth_key` VALUES (65867,'Win','x64','WoWC',_binary 'úkw–3¼nV\õ\È;y');
INSERT INTO `build_auth_key` VALUES (65893,'Mac','A64','WoW',_binary '\óV|\'\åeAüQ0¸\'\ì\×');
INSERT INTO `build_auth_key` VALUES (65893,'Mac','A64','WoWC',_binary '¹ g‘«¦\Ôml9\'O’');
INSERT INTO `build_auth_key` VALUES (65893,'Mac','x64','WoW',_binary '‹ˆY°\Ö\ã…Rf²0\æ®]');
INSERT INTO `build_auth_key` VALUES (65893,'Mac','x64','WoWC',_binary '¯•Á\Ù\ç\æ\ó•GYÁ\Í/„');
INSERT INTO `build_auth_key` VALUES (65893,'Win','A64','WoW',_binary '2Jˆ:\Ï6ª\ëffgC\óG');
INSERT INTO `build_auth_key` VALUES (65893,'Win','x64','WoW',_binary 'USu[ —†+®A•Û‰');
INSERT INTO `build_auth_key` VALUES (65893,'Win','x64','WoWC',_binary '²¯\ár/l\Â\ğûY{2R<');
INSERT INTO `build_auth_key` VALUES (65940,'Mac','A64','WoW',_binary 'ZDz·\È5¤´Õ°C@\Å: ');
INSERT INTO `build_auth_key` VALUES (65940,'Mac','A64','WoWC',_binary '-\Ô\ã\ô˜|,ı²N\×');
INSERT INTO `build_auth_key` VALUES (65940,'Mac','x64','WoW',_binary 'ı„\âúvT¥CØµ\ó\é\ò');
INSERT INTO `build_auth_key` VALUES (65940,'Mac','x64','WoWC',_binary '\Ş]X\ñ\å\íˆR#Q\Øašg');
INSERT INTO `build_auth_key` VALUES (65940,'Win','A64','WoW',_binary '\İÃŸXa\ğ\óß’i\Z\Z¯t');
INSERT INTO `build_auth_key` VALUES (65940,'Win','x64','WoW',_binary '˜¹5\ÙI\õ\Ó\Z™Í¶TXqş±');
INSERT INTO `build_auth_key` VALUES (65940,'Win','x64','WoWC',_binary '\ô\î\ß\\&©‹e\Æø#ƒ\Ş');
INSERT INTO `build_auth_key` VALUES (66017,'Mac','A64','WoW',_binary ':b»Ù\ã§~3Á²\n');
INSERT INTO `build_auth_key` VALUES (66017,'Mac','A64','WoWC',_binary '_œ\ñI¨úA8‹¶\ó¶\õ');
INSERT INTO `build_auth_key` VALUES (66017,'Mac','x64','WoW',_binary '>º·ªX*-À&†ûá±¼\"');
INSERT INTO `build_auth_key` VALUES (66017,'Mac','x64','WoWC',_binary '0´y\ÔĞ¢«4\à\ö	u\×m­¾');
INSERT INTO `build_auth_key` VALUES (66017,'Win','A64','WoW',_binary 'KT\ã	š½\É\r‡şv\Æ\öpQ');
INSERT INTO `build_auth_key` VALUES (66017,'Win','x64','WoW',_binary '¬\ã\ê½\Æ@\Ú\ô\äM.*¶-.');
INSERT INTO `build_auth_key` VALUES (66017,'Win','x64','WoWC',_binary '(IDCµqY|ø,2$\Í');
INSERT INTO `build_auth_key` VALUES (66044,'Mac','A64','WoW',_binary '[\ğ´¨OŸL\ØeÁ.À o¥');
INSERT INTO `build_auth_key` VALUES (66044,'Mac','A64','WoWC',_binary '±–„\ô7X\ß\ÚX2\Ş_qL');
INSERT INTO `build_auth_key` VALUES (66044,'Mac','x64','WoW',_binary '~[]?(†m`´Ut\ãK');
INSERT INTO `build_auth_key` VALUES (66044,'Mac','x64','WoWC',_binary '«wn¯¿h‘²F“ZI\÷I');
INSERT INTO `build_auth_key` VALUES (66044,'Win','A64','WoW',_binary '¹to4×œ™\Zy1“\ñ/?B');
INSERT INTO `build_auth_key` VALUES (66044,'Win','x64','WoW',_binary '2º„\é–ø:\ïN›gqş\òC');
INSERT INTO `build_auth_key` VALUES (66044,'Win','x64','WoWC',_binary '¬IK[I¯‹,‡\ã`¨\Â');
INSERT INTO `build_auth_key` VALUES (66066,'Mac','A64','WoW',_binary 'œ¿>ü\ïşú\Ş`hYœc=');
INSERT INTO `build_auth_key` VALUES (66066,'Mac','A64','WoWC',_binary '7\õ¦™Ó”¯µeM\ò\Ô-ø“');
INSERT INTO `build_auth_key` VALUES (66066,'Mac','x64','WoW',_binary 'C\ÉCRø–/\Ñ6™¦P\ã');
INSERT INTO `build_auth_key` VALUES (66066,'Mac','x64','WoWC',_binary '±\ÂWT°2¥\Ô\Â\ç>\ÚXú');
INSERT INTO `build_auth_key` VALUES (66066,'Win','A64','WoW',_binary 'S\Ì7¡~8¾\òp\ç\Ó\Í%\ä');
INSERT INTO `build_auth_key` VALUES (66066,'Win','x64','WoW',_binary 'ZD®\ê\òÿ_h\Â\Ú\Ïr');
INSERT INTO `build_auth_key` VALUES (66066,'Win','x64','WoWC',_binary '\Ë@;f\Û4\Üp:1‚@ıs: ');
INSERT INTO `build_auth_key` VALUES (66102,'Mac','A64','WoW',_binary '˜Ö¢\Õ\'ÿŠW\÷„Ï™');
INSERT INTO `build_auth_key` VALUES (66102,'Mac','A64','WoWC',_binary 'a\ô\Ì‹hH^`…>G‡ø');
INSERT INTO `build_auth_key` VALUES (66102,'Mac','x64','WoW',_binary '˜\ë*OH\Ü:pt0\ß8¢\í');
INSERT INTO `build_auth_key` VALUES (66102,'Mac','x64','WoWC',_binary 'SEs	\äm)úR»—\Ñ');
INSERT INTO `build_auth_key` VALUES (66102,'Win','A64','WoW',_binary '»€”\í\ô”(5\\ú+u4*');
INSERT INTO `build_auth_key` VALUES (66102,'Win','x64','WoW',_binary 'œ^F¸w8ØŸ\è¢|:\Ã\óu');
INSERT INTO `build_auth_key` VALUES (66102,'Win','x64','WoWC',_binary '½\ã#\İv(\Ç+İ•:S\Ï\õ>');
INSERT INTO `build_auth_key` VALUES (66192,'Mac','A64','WoW',_binary 'ŠjOù\ğªT\æŸ.\rI;ˆ');
INSERT INTO `build_auth_key` VALUES (66192,'Mac','A64','WoWC',_binary 'hff>¼rû–NP¶=$');
INSERT INTO `build_auth_key` VALUES (66192,'Mac','x64','WoW',_binary '5v\ñHr#\Ò6\ÓwmšF‘');
INSERT INTO `build_auth_key` VALUES (66192,'Mac','x64','WoWC',_binary '\ğ\à‡\n›û©®\İ¶Dµ£t');
INSERT INTO `build_auth_key` VALUES (66192,'Win','A64','WoW',_binary 'r\ñßƒ\Õ\ß\Ò\ğ0K[+)');
INSERT INTO `build_auth_key` VALUES (66192,'Win','x64','WoW',_binary '\ì¹Ù¸\É*ù„×œ\å');
INSERT INTO `build_auth_key` VALUES (66192,'Win','x64','WoWC',_binary 'è¢¸\í\Ï\ÊGÜ …\Ö\ì\ì4q');
INSERT INTO `build_auth_key` VALUES (66198,'Mac','A64','WoW',_binary 'C\è`\ß\È\ë*\óW0X\İOŸx');
INSERT INTO `build_auth_key` VALUES (66198,'Mac','A64','WoWC',_binary 'û‹¢ ­Š‰.s\á\ö­‡T\÷5');
INSERT INTO `build_auth_key` VALUES (66198,'Mac','x64','WoW',_binary 'G€¤À~û\õ8û!,œ');
INSERT INTO `build_auth_key` VALUES (66198,'Mac','x64','WoWC',_binary '®\ö D$–W\ÕN\â\í{\"¢\Í');
INSERT INTO `build_auth_key` VALUES (66198,'Win','A64','WoW',_binary 'hO®\ÓO¦#…K\Ñg2\ã');
INSERT INTO `build_auth_key` VALUES (66198,'Win','x64','WoW',_binary 'dÁ\Ë\õ›\È\î›füÕ¥¡O{');
INSERT INTO `build_auth_key` VALUES (66198,'Win','x64','WoWC',_binary '`°™\É\ñ\Î*.H©™ 	;µ');
INSERT INTO `build_auth_key` VALUES (66220,'Mac','A64','WoW',_binary 'C…\íE›L`\í\Èp\É\î\áz');
INSERT INTO `build_auth_key` VALUES (66220,'Mac','A64','WoWC',_binary '»‘a•\óO\ó}#0h');
INSERT INTO `build_auth_key` VALUES (66220,'Mac','x64','WoW',_binary '\æ—9\÷–S“0\÷\çfÂu');
INSERT INTO `build_auth_key` VALUES (66220,'Mac','x64','WoWC',_binary '­\ä{1€Ê­\ä\Ã\àQ|øO');
INSERT INTO `build_auth_key` VALUES (66220,'Win','A64','WoW',_binary '\ó‡\Ìd¶(™9W×²&(•');
INSERT INTO `build_auth_key` VALUES (66220,'Win','x64','WoW',_binary 'ˆ\ÃD`\âe¶/¦\ñ\ÊÎ\ã');
INSERT INTO `build_auth_key` VALUES (66220,'Win','x64','WoWC',_binary '›</¹\é\ò@_*\ÖÍ¨N');
INSERT INTO `build_auth_key` VALUES (66263,'Mac','A64','WoW',_binary '!®=¨\İ=h5Â»¹(‚•');
INSERT INTO `build_auth_key` VALUES (66263,'Mac','A64','WoWC',_binary 'z}1Âº.Ø‹\æ\âE');
INSERT INTO `build_auth_key` VALUES (66263,'Mac','x64','WoW',_binary ']®_†i\Z¸†´Š‘1<@');
INSERT INTO `build_auth_key` VALUES (66263,'Mac','x64','WoWC',_binary '´\Óu}i\Õ\ÂD°)\äAµ\Ğ\0');
INSERT INTO `build_auth_key` VALUES (66263,'Win','A64','WoW',_binary '¨\ğß£\Ä\ïc\ÂTeÆšı\×');
INSERT INTO `build_auth_key` VALUES (66263,'Win','x64','WoW',_binary '\Ô\Û6\Ü5[_›¶­´Y\Ó');
INSERT INTO `build_auth_key` VALUES (66263,'Win','x64','WoWC',_binary 'TÅ·ŸŒ%\Ê3~Gˆ\'');
INSERT INTO `build_auth_key` VALUES (66337,'Mac','A64','WoW',_binary '\îù€R¦K]\Ñc)tC\ä<');
INSERT INTO `build_auth_key` VALUES (66337,'Mac','A64','WoWC',_binary '¦ûú¹‰j\äÃ­®\÷˜\Òe');
INSERT INTO `build_auth_key` VALUES (66337,'Mac','x64','WoW',_binary '‹\Ô\Ü\Ñ>w9^›8ü\ê£ü\ì');
INSERT INTO `build_auth_key` VALUES (66337,'Mac','x64','WoWC',_binary '5&¿m\Ï\Ô9=œAÖ„‹');
INSERT INTO `build_auth_key` VALUES (66337,'Win','A64','WoW',_binary '@ù™\õ¦Œw6\Û:6\ÍÍ>');
INSERT INTO `build_auth_key` VALUES (66337,'Win','x64','WoW',_binary 'g’Ó‹ˆ\éEWi\ã“ph');
INSERT INTO `build_auth_key` VALUES (66337,'Win','x64','WoWC',_binary 'ÿm \õ¹z\ë\ã\ëV¬.ÁÂ¥\Û');
INSERT INTO `build_auth_key` VALUES (66384,'Mac','A64','WoW',_binary '‡\ÑoÆ¤Ë£„…1jrÅ¤x');
INSERT INTO `build_auth_key` VALUES (66384,'Mac','A64','WoWC',_binary '¿;\ËQ.L¡‘}\å¾Á\Ù@C');
INSERT INTO `build_auth_key` VALUES (66384,'Mac','x64','WoW',_binary 'ø\n3»&Ş‰uŸ¨Œk[\ë');
INSERT INTO `build_auth_key` VALUES (66384,'Mac','x64','WoWC',_binary '&	şRÇ®{4»±- ©™n½');
INSERT INTO `build_auth_key` VALUES (66384,'Win','A64','WoW',_binary '\ì„\èda\Ç;\æ‘\÷v\÷&\á');
INSERT INTO `build_auth_key` VALUES (66384,'Win','x64','WoW',_binary '\Ò\Ğ\n\ïŒ6¶p\ÂU\à’\â/');
INSERT INTO `build_auth_key` VALUES (66384,'Win','x64','WoWC',_binary 'ª\õ]T††E\Übº¼\Ñ,');
INSERT INTO `build_auth_key` VALUES (66431,'Mac','A64','WoW',_binary 'ˆÚ¯Š¸\Ç×–l™S\Úkˆ‘\Ñ');
INSERT INTO `build_auth_key` VALUES (66431,'Mac','A64','WoWC',_binary 'Œ\÷Ju.!CwüŸV\Å');
INSERT INTO `build_auth_key` VALUES (66431,'Mac','x64','WoW',_binary 'z\È\õ½¿™‹N\ÒrŒşb\Ş]');
INSERT INTO `build_auth_key` VALUES (66431,'Mac','x64','WoWC',_binary '¦\Ş\òµ,H<‘SKú\ö\"¦”');
INSERT INTO `build_auth_key` VALUES (66431,'Win','A64','WoW',_binary 'v\ì1T\æ\ó\r·ˆ´qS\ñ/');
INSERT INTO `build_auth_key` VALUES (66431,'Win','x64','WoW',_binary '\\\î2\ä	Ş¶l\óû\Æ(0üü\\');
INSERT INTO `build_auth_key` VALUES (66431,'Win','x64','WoWC',_binary '\Ç-ù4¬£”Ymûf‚&|');
INSERT INTO `build_auth_key` VALUES (66527,'Mac','A64','WoW',_binary 'ù\'udP]5:’$M');
INSERT INTO `build_auth_key` VALUES (66527,'Mac','A64','WoWC',_binary 'G\\	\ğ‘!Ë†Ò²%¾>k');
INSERT INTO `build_auth_key` VALUES (66527,'Mac','x64','WoW',_binary '\"ª›\öK!\éA\é#¥\âE+');
INSERT INTO `build_auth_key` VALUES (66527,'Mac','x64','WoWC',_binary '\ô\n\ç6{\ä·B©\â\\©=¹');
INSERT INTO `build_auth_key` VALUES (66527,'Win','A64','WoW',_binary '\é(\à‘\È#ùuƒ•Ÿ>h.');
INSERT INTO `build_auth_key` VALUES (66527,'Win','x64','WoW',_binary 'Ë®+\Ñ„•\Ì\á¯U\ö\ĞP°');
INSERT INTO `build_auth_key` VALUES (66527,'Win','x64','WoWC',_binary '”\Óm»5\ôıM—¥7‹');
INSERT INTO `build_auth_key` VALUES (66562,'Mac','A64','WoW',_binary 'hB\"i²“w\ç\ïWë¯\Ô');
INSERT INTO `build_auth_key` VALUES (66562,'Mac','A64','WoWC',_binary '8%‘\'“~›;\ã\î3W\Ä\r');
INSERT INTO `build_auth_key` VALUES (66562,'Mac','x64','WoW',_binary '\ÓC™¯\Â\×h\ÌqE¼^\Æ');
INSERT INTO `build_auth_key` VALUES (66562,'Mac','x64','WoWC',_binary 'p„\Ï~\ö¾¸\é­Q´-°8');
INSERT INTO `build_auth_key` VALUES (66562,'Win','A64','WoW',_binary '´\ZwŸ\î\É~¤„3w\Æ3ƒ');
INSERT INTO `build_auth_key` VALUES (66562,'Win','x64','WoW',_binary '\é\Ò\ã¦g§^ABr¯yı');
INSERT INTO `build_auth_key` VALUES (66562,'Win','x64','WoWC',_binary '\÷…•A&\0[)Oü7?\'g¯Á');
INSERT INTO `build_auth_key` VALUES (66666,'Mac','A64','WoW',_binary 'c\Ú;\Ç\èN\îX\ÇP8cùl‡');
INSERT INTO `build_auth_key` VALUES (66666,'Mac','A64','WoWC',_binary '\0\ÄÌ«>\öO@¬P\ô?');
INSERT INTO `build_auth_key` VALUES (66666,'Mac','x64','WoW',_binary 'Ğœ\Ão6f2\ã¶\ã\ë\ë');
INSERT INTO `build_auth_key` VALUES (66666,'Mac','x64','WoWC',_binary 'Š˜Æ7Uı;BAV›v©+?');
INSERT INTO `build_auth_key` VALUES (66666,'Win','A64','WoW',_binary '[\Â/>Dù\â\\$¦\êG¡›');
INSERT INTO `build_auth_key` VALUES (66666,'Win','x64','WoW',_binary '®/f›†¾\È2U9ù~s8');
INSERT INTO `build_auth_key` VALUES (66666,'Win','x64','WoWC',_binary 'Ù½Z\í]Ê»w‚=š\Ó>');
INSERT INTO `build_auth_key` VALUES (66709,'Mac','A64','WoW',_binary '9k’-4<P¾¾¿\ÇM0\í\Ã');
INSERT INTO `build_auth_key` VALUES (66709,'Mac','A64','WoWC',_binary '\"\Ù-å±)—®!\Û~\n{');
INSERT INTO `build_auth_key` VALUES (66709,'Mac','x64','WoW',_binary '†\Ñgz\åj^€ÿj-¥{?');
INSERT INTO `build_auth_key` VALUES (66709,'Mac','x64','WoWC',_binary '	úº^ÿ«I‘¶|€#…š');
INSERT INTO `build_auth_key` VALUES (66709,'Win','A64','WoW',_binary 'W²\Z4{l3\0Í¹€b');
INSERT INTO `build_auth_key` VALUES (66709,'Win','x64','WoW',_binary '¥¾mSy\Ê\Ş\â×\Ï+e[');
INSERT INTO `build_auth_key` VALUES (66709,'Win','x64','WoWC',_binary '0™-#t©½¼\ïB°8:');
INSERT INTO `build_auth_key` VALUES (66838,'Mac','A64','WoW',_binary '\r\ğ‡\Î\É\Ö<@Í¤K\íM');
INSERT INTO `build_auth_key` VALUES (66838,'Mac','A64','WoWC',_binary ']ü?ÁƒşC¾\ê,\ÖS\Ğ');
INSERT INTO `build_auth_key` VALUES (66838,'Mac','x64','WoW',_binary 'o»š\ã?\óK²jıœŠ¹\ê');
INSERT INTO `build_auth_key` VALUES (66838,'Mac','x64','WoWC',_binary '\ó‘\åw¹>O@\\·A\r\ä\Ò');
INSERT INTO `build_auth_key` VALUES (66838,'Win','A64','WoW',_binary '\ò\Ø2ZÍ¢b\ä|©Nšt\â');
INSERT INTO `build_auth_key` VALUES (66838,'Win','x64','WoW',_binary '3\ğşPù[/\ísØ¾\öt');
INSERT INTO `build_auth_key` VALUES (66838,'Win','x64','WoWC',_binary '\Ëv\â{)S­qU©\Û\é$‚');
INSERT INTO `build_auth_key` VALUES (67088,'Mac','A64','WoW',_binary '3Î‡0­ \ó9c\÷\çe—8^ú');
INSERT INTO `build_auth_key` VALUES (67088,'Mac','A64','WoWC',_binary '…úR8fqb\Ô\ó³qµ®p');
INSERT INTO `build_auth_key` VALUES (67088,'Mac','x64','WoW',_binary '· ?P\Ê\íjLa™+”2');
INSERT INTO `build_auth_key` VALUES (67088,'Mac','x64','WoWC',_binary '\å#b\×Wb\0)a\Çj\ã–~');
INSERT INTO `build_auth_key` VALUES (67088,'Win','A64','WoW',_binary '[øme\Â1¥\Í8~²¥$');
INSERT INTO `build_auth_key` VALUES (67088,'Win','x64','WoW',_binary 'ÒµG>Œ\ô‚X9\ñh‹º>Õ°');
INSERT INTO `build_auth_key` VALUES (67088,'Win','x64','WoWC',_binary '\×Fš0\ë•—\ÄCt=t„{');
INSERT INTO `build_auth_key` VALUES (67114,'Mac','A64','WoW',_binary '\Zbù\Ñ_’mj…ß‘¼\ç\ñ');
INSERT INTO `build_auth_key` VALUES (67114,'Mac','A64','WoWC',_binary 'Ò,5úo\ä\ÃÀ\×h”CF');
INSERT INTO `build_auth_key` VALUES (67114,'Mac','x64','WoW',_binary 'Y@r1°‚\÷Xub¬@Æ™&');
INSERT INTO `build_auth_key` VALUES (67114,'Mac','x64','WoWC',_binary '\â_\Ó?t†\ò&}#\Õ\'\Ğş');
INSERT INTO `build_auth_key` VALUES (67114,'Win','A64','WoW',_binary '\'P\ìa/.0Z Áj_¨');
INSERT INTO `build_auth_key` VALUES (67114,'Win','x64','WoW',_binary 'C\õ˜\Ä\æ|/dJViÀ\ßA');
INSERT INTO `build_auth_key` VALUES (67114,'Win','x64','WoWC',_binary 'ˆ®–[\Ñ~\ãq‰½sl_}{');
INSERT INTO `build_auth_key` VALUES (67186,'Mac','A64','WoW',_binary '\n3\Â0¥‰\\DŠ¾™\Ï*');
INSERT INTO `build_auth_key` VALUES (67186,'Mac','A64','WoWC',_binary 'G™m`\×o\ô5hƒK\Ì?T');
INSERT INTO `build_auth_key` VALUES (67186,'Mac','x64','WoW',_binary '\ó\"\"İ›0f\0=\Éxs');
INSERT INTO `build_auth_key` VALUES (67186,'Mac','x64','WoWC',_binary '\nq¥ÿ\çÁ½\ã\ÎY\r>\ò');
INSERT INTO `build_auth_key` VALUES (67186,'Win','A64','WoW',_binary '§qyøŠa?–\éoœ]\'ƒ');
INSERT INTO `build_auth_key` VALUES (67186,'Win','x64','WoW',_binary '»\è¾a&Ÿ%Wø\ã\Õ\Z“\×');
INSERT INTO `build_auth_key` VALUES (67186,'Win','x64','WoWC',_binary 'VM™V&>HaEfG\í|');
INSERT INTO `build_auth_key` VALUES (67235,'Mac','A64','WoW',_binary '\ìn\Ñ,D\Ü\É\ñ¢2¥¨Q\Ş');
INSERT INTO `build_auth_key` VALUES (67235,'Mac','A64','WoWC',_binary 'QÎ°\ÔtNÎx»&1Y');
INSERT INTO `build_auth_key` VALUES (67235,'Mac','x64','WoW',_binary 'V,H\òx\î\÷±µ^§\È');
INSERT INTO `build_auth_key` VALUES (67235,'Mac','x64','WoWC',_binary '—¨rp\Ñ¥\Öı?\Évo§M');
INSERT INTO `build_auth_key` VALUES (67235,'Win','A64','WoW',_binary '\èüUÅ°‚Zb0½\ËNa');
INSERT INTO `build_auth_key` VALUES (67235,'Win','x64','WoW',_binary 'B\Ú+\æ|¡û)p”]‰¯~');
INSERT INTO `build_auth_key` VALUES (67235,'Win','x64','WoWC',_binary 'q¸\ó~[\ò\rB‹‹\ô\İ\Ë\×');
INSERT INTO `build_auth_key` VALUES (67314,'Mac','A64','WoW',_binary 'n\íh\Ãú\éke\ïÁzƒ\÷');
INSERT INTO `build_auth_key` VALUES (67314,'Mac','A64','WoWC',_binary '¤\ì¤\Çcªfo†#\Ä8¦:t');
INSERT INTO `build_auth_key` VALUES (67314,'Mac','x64','WoW',_binary 'b’\ß\î>\È\ñ|f¿Öšy¢');
INSERT INTO `build_auth_key` VALUES (67314,'Mac','x64','WoWC',_binary 'I¶¨Š¡@Wÿ¶Ø¬Lª');
INSERT INTO `build_auth_key` VALUES (67314,'Win','A64','WoW',_binary '\óƒ9a$7º\ŞVı#\Ú\ç\Õv');
INSERT INTO `build_auth_key` VALUES (67314,'Win','x64','WoW',_binary '¤\í\îÿ\ó7+‰=\Ë7†}\æŸ\Ñ');
INSERT INTO `build_auth_key` VALUES (67314,'Win','x64','WoWC',_binary 'bIŒ’Bœ©º¡=,¨\Êk');
INSERT INTO `build_auth_key` VALUES (67403,'Mac','A64','WoW',_binary '\îW?§)©ù\à\Íû\Ú[\â+');
INSERT INTO `build_auth_key` VALUES (67403,'Mac','A64','WoWC',_binary 'Z‰\É \ZÒ²§\Ú7\Ğ\ç³');
INSERT INTO `build_auth_key` VALUES (67403,'Mac','x64','WoW',_binary '–4z‚\îX¹—A\òE%\É');
INSERT INTO `build_auth_key` VALUES (67403,'Mac','x64','WoWC',_binary '°A¿§?¿™¦QSh¥Ş¿¢');
INSERT INTO `build_auth_key` VALUES (67403,'Win','A64','WoW',_binary '\æ?ÁI\ğ\Ëi‡ŸÇ—°W');
INSERT INTO `build_auth_key` VALUES (67403,'Win','x64','WoW',_binary '¸\æE\âA\î<)‡(\ëF ');
INSERT INTO `build_auth_key` VALUES (67403,'Win','x64','WoWC',_binary '^\Ø:•¹?­2üo\Ï}\ØË”');
INSERT INTO `build_auth_key` VALUES (67451,'Mac','A64','WoW',_binary 'ˆ’\ÏU9\Ú\Úûÿq\à»\â€');
INSERT INTO `build_auth_key` VALUES (67451,'Mac','A64','WoWC',_binary 'A`Á	\Ø\Ş3‰@\í/ü');
INSERT INTO `build_auth_key` VALUES (67451,'Mac','x64','WoW',_binary 'œ©\Ğ\\K wKD”\á\×\á');
INSERT INTO `build_auth_key` VALUES (67451,'Mac','x64','WoWC',_binary 'A\İ*7\\¥\ĞØŒ4œR\ÅZ');
INSERT INTO `build_auth_key` VALUES (67451,'Win','A64','WoW',_binary '\\™^$)\Ş5şn«‡®\Øu');
INSERT INTO `build_auth_key` VALUES (67451,'Win','x64','WoW',_binary 'yˆ\Z‹d‚½sÛ™ùÆ”V\ê');
INSERT INTO `build_auth_key` VALUES (67451,'Win','x64','WoWC',_binary '+ ü\Ä\í²R\ÈùÁ\àZk');
INSERT INTO `build_auth_key` VALUES (67602,'Mac','A64','WoW',_binary '‰\âÎ¶®(™}°;³2¾Å');
INSERT INTO `build_auth_key` VALUES (67602,'Mac','A64','WoWC',_binary 'Á½,°\ê\ÄXBzm\î\ÜH ');
INSERT INTO `build_auth_key` VALUES (67602,'Mac','x64','WoW',_binary 'J$:šI“o\å\Ê\ÇO’');
INSERT INTO `build_auth_key` VALUES (67602,'Mac','x64','WoWC',_binary '1(›²[§§x\Ô\åOÿc‚–');
INSERT INTO `build_auth_key` VALUES (67602,'Win','A64','WoW',_binary '.,\ç¢Uï†—\Ğ{Xk8 ');
INSERT INTO `build_auth_key` VALUES (67602,'Win','x64','WoW',_binary 'sd\òLßŒ\ì\èn\Ø\Ö\É\Òm\à');
INSERT INTO `build_auth_key` VALUES (67602,'Win','x64','WoWC',_binary '±\Ö[m\"Ğ‹·\É‰I<q');
INSERT INTO `build_auth_key` VALUES (67823,'Mac','A64','WoW',_binary '—`\ÂI3\Òt\åÀ—Ï˜\Ş');
INSERT INTO `build_auth_key` VALUES (67823,'Mac','A64','WoWC',_binary '‹®/=\"_CH¥\õg\ñ@');
INSERT INTO `build_auth_key` VALUES (67823,'Mac','x64','WoW',_binary '\ây¼g‰0U\Æ5q~¥¬\0');
INSERT INTO `build_auth_key` VALUES (67823,'Mac','x64','WoWC',_binary 'jŒ~‚yÀ‘D(\Ø.\õ§|');
INSERT INTO `build_auth_key` VALUES (67823,'Win','A64','WoW',_binary '\rc­\n±X˜Š\à…|\Ô%¡ı');
INSERT INTO `build_auth_key` VALUES (67823,'Win','x64','WoW',_binary '\\\á\í|1\ÂGy6Zº2%;\n');
INSERT INTO `build_auth_key` VALUES (67823,'Win','x64','WoWC',_binary 'kxo1)G~(]<m5\ô');
INSERT INTO `build_auth_key` VALUES (68182,'Mac','A64','WoW',_binary '\Ó\n(\Ò7‡@’Ø•\ì%\á&');
INSERT INTO `build_auth_key` VALUES (68182,'Mac','A64','WoWC',_binary 'À\İl^Œ¥«u/\ëu\\şFV‡');
INSERT INTO `build_auth_key` VALUES (68182,'Mac','x64','WoW',_binary 'Cü\ÇC\Û\ï\nÁ\Ş\â\ò\×	\Ä');
INSERT INTO `build_auth_key` VALUES (68182,'Mac','x64','WoWC',_binary 'WHµÊ\ì\÷3inˆ\Â\Ü\Ú');
INSERT INTO `build_auth_key` VALUES (68182,'Win','A64','WoW',_binary '¥şü]k5\Ô\nS®2C\Ú');
INSERT INTO `build_auth_key` VALUES (68182,'Win','x64','WoW',_binary '\İN&j.[\äÁ‹¥ÿ\ì‡m');
INSERT INTO `build_auth_key` VALUES (68182,'Win','x64','WoWC',_binary '\Ê\ì\Ä\à¶><ª9\ÌdY°‹y');
INSERT INTO `build_auth_key` VALUES (68232,'Mac','A64','WoW',_binary '”yt#kÙZY\ZÁ³');
INSERT INTO `build_auth_key` VALUES (68232,'Mac','A64','WoWC',_binary 'û/\ğ?øqÿ\Z·°³l/%');
INSERT INTO `build_auth_key` VALUES (68232,'Mac','x64','WoW',_binary 'Ø•¯¸û~\äŠ0 \îmÙ”');
INSERT INTO `build_auth_key` VALUES (68232,'Mac','x64','WoWC',_binary ')C¶@•‚6ƒ\Ú\Â');
INSERT INTO `build_auth_key` VALUES (68232,'Win','A64','WoW',_binary '%\0ÿ\é\Î&µ¤’À9\Â');
INSERT INTO `build_auth_key` VALUES (68232,'Win','x64','WoW',_binary 'Gzs\áv!»Ê…\Ö\õ³·');
INSERT INTO `build_auth_key` VALUES (68232,'Win','x64','WoWC',_binary '\ĞXxAz¨\Ò*a\ôøœ’q\Û');
INSERT INTO `build_auth_key` VALUES (68235,'Mac','A64','WoW',_binary 'ûîŸ²­\ÌZ¬B\×3V\Ğ\õ');
INSERT INTO `build_auth_key` VALUES (68235,'Mac','A64','WoWC',_binary '¦qY?—T¬“‹u1m\é¼R');
INSERT INTO `build_auth_key` VALUES (68235,'Mac','x64','WoW',_binary '*%Ë³\Än\Ú\ÂRm\\\Òû\ær˜');
INSERT INTO `build_auth_key` VALUES (68235,'Mac','x64','WoWC',_binary 'M³œjĞº²D\õ*u\ÑA7');
INSERT INTO `build_auth_key` VALUES (68235,'Win','A64','WoW',_binary '—Œ_qf\nË¹uV¢.');
INSERT INTO `build_auth_key` VALUES (68235,'Win','x64','WoW',_binary '\òaIı2y¸\Õ\ñL\â²ı)<');
INSERT INTO `build_auth_key` VALUES (68235,'Win','x64','WoWC',_binary '·iv\îq|\Ü\Â%H}j£\Ü');
INSERT INTO `build_auth_key` VALUES (68256,'Mac','A64','WoW',_binary '=‚£\Ö+=\ìµRP‰\Ç');
INSERT INTO `build_auth_key` VALUES (68256,'Mac','A64','WoWC',_binary 'š@½±g¸l\núŒ‡\ìBW³');
INSERT INTO `build_auth_key` VALUES (68256,'Mac','x64','WoW',_binary '•\ß\â\ó‡c\Úc\Îh)›\Ïk');
INSERT INTO `build_auth_key` VALUES (68256,'Mac','x64','WoWC',_binary '~5\Û8ü\ær”À\ét,I3ü');
INSERT INTO `build_auth_key` VALUES (68256,'Win','A64','WoW',_binary 'F£mÃ±B}¼J¸F·');
INSERT INTO `build_auth_key` VALUES (68256,'Win','x64','WoW',_binary 'Ê’øü\Ä+¨\ëV™4\öÚ…');
INSERT INTO `build_auth_key` VALUES (68256,'Win','x64','WoWC',_binary 'p\Ñ\Ëj\ØO\Éei\É8¢ˆlc');
INSERT INTO `build_auth_key` VALUES (68275,'Mac','A64','WoW',_binary '\í\Ú8\òa¼Jh\õ/™‰¦\Z\Û');
INSERT INTO `build_auth_key` VALUES (68275,'Mac','A64','WoWC',_binary '–\Ñx…\Ô\ÜT‡~\Û\ìN…|©');
INSERT INTO `build_auth_key` VALUES (68275,'Mac','x64','WoW',_binary '[8\ì¹s\ô‚‡r\Ò{!ø“*');
INSERT INTO `build_auth_key` VALUES (68275,'Mac','x64','WoWC',_binary '}²›\'¾ı¶¥©È•f{g');
INSERT INTO `build_auth_key` VALUES (68275,'Win','A64','WoW',_binary '\Ã¯@\ÙúM*±\÷TÄ®\Ï~');
INSERT INTO `build_auth_key` VALUES (68275,'Win','x64','WoW',_binary 'm¿©m\Â1\ç\Â\ÖtIŒŠ\Ì');
INSERT INTO `build_auth_key` VALUES (68275,'Win','x64','WoWC',_binary 'xF§£Ü\ê5“Sa\äR ');
INSERT INTO `build_auth_key` VALUES (68367,'Mac','A64','WoW',_binary '\İz «\×\'Œ«H™\éD_');
INSERT INTO `build_auth_key` VALUES (68367,'Mac','A64','WoWC',_binary '-\ØK±e\Û@aXjÁ‡\Ú');
INSERT INTO `build_auth_key` VALUES (68367,'Mac','x64','WoW',_binary '*&\Å\â»qÄ¹qRy\Ì\å\ò');
INSERT INTO `build_auth_key` VALUES (68367,'Mac','x64','WoWC',_binary 'O·\÷µ…ˆø>\é³U\õ7\Ö');
INSERT INTO `build_auth_key` VALUES (68367,'Win','A64','WoW',_binary '\îÖŠ«›9on4û\÷\Ñ\Úe>');
INSERT INTO `build_auth_key` VALUES (68367,'Win','x64','WoW',_binary '–·\â7K7ÁEø\Z‹-');
INSERT INTO `build_auth_key` VALUES (68367,'Win','x64','WoWC',_binary '£_¹\ô\Z£E‰I¾\á\Ç\ç\ä');
INSERT INTO `build_auth_key` VALUES (68453,'Mac','A64','WoW',_binary '\÷Z0î†€C+Ì¹\è/l¬*');
INSERT INTO `build_auth_key` VALUES (68453,'Mac','A64','WoWC',_binary 'e\ØcŒŠ\ê-”>§\É\\\ä');
INSERT INTO `build_auth_key` VALUES (68453,'Mac','x64','WoW',_binary '¸‚S0d†X¿9C‘t\Î&');
INSERT INTO `build_auth_key` VALUES (68453,'Mac','x64','WoWC',_binary 'ˆG~9\è<*>\'`?˜*g');
INSERT INTO `build_auth_key` VALUES (68453,'Win','A64','WoW',_binary ')\åÈƒq]ıbµ{Gg');
INSERT INTO `build_auth_key` VALUES (68453,'Win','x64','WoW',_binary 'K\çb\í\ä3T\ËitLzA\ã');
INSERT INTO `build_auth_key` VALUES (68453,'Win','x64','WoWC',_binary 'Re@}~”3\Ôg\Ê\ğx_');
INSERT INTO `build_auth_key` VALUES (68887,'Mac','A64','WoW',_binary 'y\Z\ç2\Í4Å†pø\ç`†\Ã\ç');
INSERT INTO `build_auth_key` VALUES (68887,'Mac','A64','WoWC',_binary 'Ÿ(\ï\Ø\Ä$’\Ò&7™Ï±');
INSERT INTO `build_auth_key` VALUES (68887,'Mac','x64','WoW',_binary 'p4h\é\Ò\Ó\Ã!–©\×p\Õ&');
INSERT INTO `build_auth_key` VALUES (68887,'Mac','x64','WoWC',_binary '¤ı:¨\Ç‘¨¸vı’;');
INSERT INTO `build_auth_key` VALUES (68887,'Win','A64','WoW',_binary 'V\Ì4c5zÀ_\\)\á—\"@');
INSERT INTO `build_auth_key` VALUES (68887,'Win','x64','WoW',_binary '_\ïŠ^P\Éx\r¿¢•ùB');
INSERT INTO `build_auth_key` VALUES (68887,'Win','x64','WoWC',_binary '‹‡]†\Ü\r\à.l05\Ä3lA');
INSERT INTO `build_auth_key` VALUES (68974,'Mac','A64','WoW',_binary '…\İ@\Ş\ãZ¹“A}µ%¶·z)');
INSERT INTO `build_auth_key` VALUES (68974,'Mac','A64','WoWC',_binary 'üM\Ê]\ò\è\\>\Ì)·£x2»');
INSERT INTO `build_auth_key` VALUES (68974,'Mac','x64','WoW',_binary 'sa­µcÀ\ß­Â—\õ¶±');
INSERT INTO `build_auth_key` VALUES (68974,'Mac','x64','WoWC',_binary '\ÄWX†^y°¬5´‰ÀÆ˜‘');
INSERT INTO `build_auth_key` VALUES (68974,'Win','A64','WoW',_binary '”\r×Ÿ‹\ğî š^®‚\0AP');
INSERT INTO `build_auth_key` VALUES (68974,'Win','x64','WoW',_binary '\÷h\Ì\ê#6£—==3\í\ï{v');
INSERT INTO `build_auth_key` VALUES (68974,'Win','x64','WoWC',_binary '\æ	\Ì$\ï²8[ƒ\ï\×)');
INSERT INTO `build_auth_key` VALUES (69404,'Mac','A64','WoW',_binary '½ E2¸­ü\ë 9¿pH');
INSERT INTO `build_auth_key` VALUES (69404,'Mac','A64','WoWC',_binary 'wÂ”\Ò#II©H5L\Ø|ø\ìH');
INSERT INTO `build_auth_key` VALUES (69404,'Mac','x64','WoW',_binary '¬–“ı\"\0m„\Z\ì©#¯');
INSERT INTO `build_auth_key` VALUES (69404,'Mac','x64','WoWC',_binary '¤K¥±±Á€…m«7„’\íE');
INSERT INTO `build_auth_key` VALUES (69404,'Win','A64','WoW',_binary '\ÛK±Rf=¥,¿6ı.eS');
INSERT INTO `build_auth_key` VALUES (69404,'Win','x64','WoW',_binary '¸k\å\ó\ğ|$¬¸\êA\×nü');
INSERT INTO `build_auth_key` VALUES (69404,'Win','x64','WoWC',_binary 'O\å-dú»s+Xd\ï70ş');
INSERT INTO `build_auth_key` VALUES (69465,'Mac','A64','WoW',_binary '\Ù8$sODyŸ\ğş\è6ú');
INSERT INTO `build_auth_key` VALUES (69465,'Mac','A64','WoWC',_binary '#7ø\ßQ@H;[\á\ï');
INSERT INTO `build_auth_key` VALUES (69465,'Mac','x64','WoW',_binary 'Î¥‹&•\÷5×³Á\Ş\õ¤‡');
INSERT INTO `build_auth_key` VALUES (69465,'Mac','x64','WoWC',_binary '\ã\rú™•®6:R^P/ˆ');
INSERT INTO `build_auth_key` VALUES (69465,'Win','A64','WoW',_binary '\í¿½GX‘…™˜E\Ò');
INSERT INTO `build_auth_key` VALUES (69465,'Win','x64','WoW',_binary 'œ«8\ï\'¦b\ğpú9£¿~');
INSERT INTO `build_auth_key` VALUES (69465,'Win','x64','WoWC',_binary '/\Ò:¾¸—ŸZ\ó6s_\ï');
INSERT INTO `build_auth_key` VALUES (69497,'Mac','A64','WoW',_binary '­-\Õ\ãcù/™Àd3´˜\Ò');
INSERT INTO `build_auth_key` VALUES (69497,'Mac','A64','WoWC',_binary '¸~»K\÷O\Ğá¢»*]Ÿ');
INSERT INTO `build_auth_key` VALUES (69497,'Mac','x64','WoW',_binary '\Ê\nœ*$!²ƒÑ\Ğşu-‰');
INSERT INTO `build_auth_key` VALUES (69497,'Mac','x64','WoWC',_binary '*À\áÀPKjª\ñ!C—H');
INSERT INTO `build_auth_key` VALUES (69497,'Win','A64','WoW',_binary '´\àªt\Ãú­‡½Eı_¿´');
INSERT INTO `build_auth_key` VALUES (69497,'Win','x64','WoW',_binary 'S\õ€Gt\\»›Š\é\n-·R\Ì');
INSERT INTO `build_auth_key` VALUES (69497,'Win','x64','WoWC',_binary 'FCi™\Ùz¢ i?\Ş(\à');
INSERT INTO `build_auth_key` VALUES (69587,'Mac','A64','WoW',_binary '©¿\Æ*\Ì\ÙE\æZR:\Ì^\Ş');
INSERT INTO `build_auth_key` VALUES (69587,'Mac','A64','WoWC',_binary '5#\'\Ë\ß\ó/u3|6o\ê');
INSERT INTO `build_auth_key` VALUES (69587,'Mac','x64','WoW',_binary '`ú	\ìH»¬\÷yı‹üÕ—Ÿ');
INSERT INTO `build_auth_key` VALUES (69587,'Mac','x64','WoWC',_binary '\â*Õ¯7\Ë È¨£Ì€\ÙP');
INSERT INTO `build_auth_key` VALUES (69587,'Win','A64','WoW',_binary '0#Ã”‹[¼¿\ÂF');
INSERT INTO `build_auth_key` VALUES (69587,'Win','x64','WoW',_binary '\î\Ê.V>™¥W5™\Ï');
INSERT INTO `build_auth_key` VALUES (69587,'Win','x64','WoWC',_binary 'Œ×–QuGšf\æ¼\ã\ñ×¿1');
INSERT INTO `build_auth_key` VALUES (69814,'Mac','A64','WoW',_binary 'Ô—¦›\öû%¾T®#\ä\Z\Zún');
INSERT INTO `build_auth_key` VALUES (69814,'Mac','A64','WoWC',_binary '–”\Ë‹e*\Çv£ 98X');
INSERT INTO `build_auth_key` VALUES (69814,'Mac','x64','WoW',_binary 'ü\Ô\rL\èø `Y=Aİ‘');
INSERT INTO `build_auth_key` VALUES (69814,'Mac','x64','WoWC',_binary '\Âx\Ú	¯¨Å“:.%ƒù©');
INSERT INTO `build_auth_key` VALUES (69814,'Win','A64','WoW',_binary '~\ê<”È¥1N­P\å\Æ\'¥0');
INSERT INTO `build_auth_key` VALUES (69814,'Win','x64','WoW',_binary 'SHC—`\ñ\İH\İh\ß\à:\Ç');
INSERT INTO `build_auth_key` VALUES (69814,'Win','x64','WoWC',_binary '!\ô5Á\"\Åıhµ\Û;\Â\Ùª');
INSERT INTO `build_auth_key` VALUES (69875,'Mac','A64','WoW',_binary 'Ø€“h.\Çt`…‹·²iùh');
INSERT INTO `build_auth_key` VALUES (69875,'Mac','A64','WoWC',_binary '1q¨!	\"F\Ît\÷\ğaD\ÃË³');
INSERT INTO `build_auth_key` VALUES (69875,'Mac','x64','WoW',_binary '=¿jùP²Ö£g¼-y@\÷C?');
INSERT INTO `build_auth_key` VALUES (69875,'Mac','x64','WoWC',_binary ';.mB¦\å\ÊŞ‘“\ïx–\r5');
INSERT INTO `build_auth_key` VALUES (69875,'Win','A64','WoW',_binary 'Lş²@\Ğ4`³g½%');
INSERT INTO `build_auth_key` VALUES (69875,'Win','x64','WoW',_binary '\àª0­\\YXi\ñ;k‹\ì\Â1');
INSERT INTO `build_auth_key` VALUES (69875,'Win','x64','WoWC',_binary '\èÀg!ª\ò±\ÇH¬B„¥t');
INSERT INTO `build_auth_key` VALUES (69933,'Mac','A64','WoW',_binary '…_™[¨«®\È(*db>\öT');
INSERT INTO `build_auth_key` VALUES (69933,'Mac','A64','WoWC',_binary '‡u>.\Ï\ñÈQ4%\Õ');
INSERT INTO `build_auth_key` VALUES (69933,'Mac','x64','WoW',_binary 'OF¥¨\á*eN\ç\ìÿfWrV');
INSERT INTO `build_auth_key` VALUES (69933,'Mac','x64','WoWC',_binary 'U\Îúxn{­\\ˆL´*D\nL');
INSERT INTO `build_auth_key` VALUES (69933,'Win','A64','WoW',_binary '\Ô\Ñlƒ\òd\Zò­»±\æt');
INSERT INTO `build_auth_key` VALUES (69933,'Win','x64','WoW',_binary '\ŞE++\Òdƒ­”K´\ÇfI');
INSERT INTO `build_auth_key` VALUES (69933,'Win','x64','WoWC',_binary '|\Õúû\\\èjş…\Ö\í\Ş\Ç\Ïa');
/*!40000 ALTER TABLE `build_auth_key` ENABLE KEYS */;

/*!40000 ALTER TABLE `build_executable_hash` DISABLE KEYS */;
INSERT INTO `build_executable_hash` VALUES (5875,'OSX',_binary '<Ã–\ë«\ó6\õ\æg[µ\å');
INSERT INTO `build_executable_hash` VALUES (5875,'Win',_binary '•\í²|x#³c\Ëİ«V£’\ç\Ësü\Ê ');
INSERT INTO `build_executable_hash` VALUES (8606,'OSX',_binary 'Ø°\ìşSKÁº\Ñ\ÔÀ\è\î\ä™O');
INSERT INTO `build_executable_hash` VALUES (8606,'Win',_binary '1šú£\òU–‚ùÿe‹\àV%_Eo±');
INSERT INTO `build_executable_hash` VALUES (12340,'OSX',_binary '·\Ñ?\ò\ôˆ9r”a\ãø \âµıÀ4');
INSERT INTO `build_executable_hash` VALUES (12340,'Win',_binary '\ÍË½Qˆ1^kMDI-¼ú\ñV£G');
/*!40000 ALTER TABLE `build_executable_hash` ENABLE KEYS */;

/*!40000 ALTER TABLE `build_info` DISABLE KEYS */;
INSERT INTO `build_info` VALUES (5875,1,12,1,NULL);
INSERT INTO `build_info` VALUES (6005,1,12,2,NULL);
INSERT INTO `build_info` VALUES (6141,1,12,3,NULL);
INSERT INTO `build_info` VALUES (8606,2,4,3,NULL);
INSERT INTO `build_info` VALUES (9947,3,1,3,NULL);
INSERT INTO `build_info` VALUES (10505,3,2,2,'a');
INSERT INTO `build_info` VALUES (11159,3,3,0,'a');
INSERT INTO `build_info` VALUES (11403,3,3,2,NULL);
INSERT INTO `build_info` VALUES (11723,3,3,3,'a');
INSERT INTO `build_info` VALUES (12340,3,3,5,'a');
INSERT INTO `build_info` VALUES (13623,4,0,6,'a');
INSERT INTO `build_info` VALUES (13930,3,3,5,'a');
INSERT INTO `build_info` VALUES (14545,4,2,2,NULL);
INSERT INTO `build_info` VALUES (15595,4,3,4,NULL);
INSERT INTO `build_info` VALUES (19116,6,0,3,NULL);
INSERT INTO `build_info` VALUES (19243,6,0,3,NULL);
INSERT INTO `build_info` VALUES (19342,6,0,3,NULL);
INSERT INTO `build_info` VALUES (19702,6,1,0,NULL);
INSERT INTO `build_info` VALUES (19802,6,1,2,NULL);
INSERT INTO `build_info` VALUES (19831,6,1,2,NULL);
INSERT INTO `build_info` VALUES (19865,6,1,2,NULL);
INSERT INTO `build_info` VALUES (20182,6,2,0,'a');
INSERT INTO `build_info` VALUES (20201,6,2,0,NULL);
INSERT INTO `build_info` VALUES (20216,6,2,0,NULL);
INSERT INTO `build_info` VALUES (20253,6,2,0,NULL);
INSERT INTO `build_info` VALUES (20338,6,2,0,NULL);
INSERT INTO `build_info` VALUES (20444,6,2,2,NULL);
INSERT INTO `build_info` VALUES (20490,6,2,2,'a');
INSERT INTO `build_info` VALUES (20574,6,2,2,'a');
INSERT INTO `build_info` VALUES (20726,6,2,3,NULL);
INSERT INTO `build_info` VALUES (20779,6,2,3,NULL);
INSERT INTO `build_info` VALUES (20886,6,2,3,NULL);
INSERT INTO `build_info` VALUES (21355,6,2,4,NULL);
INSERT INTO `build_info` VALUES (21463,6,2,4,NULL);
INSERT INTO `build_info` VALUES (21742,6,2,4,NULL);
INSERT INTO `build_info` VALUES (22248,7,0,3,NULL);
INSERT INTO `build_info` VALUES (22293,7,0,3,NULL);
INSERT INTO `build_info` VALUES (22345,7,0,3,NULL);
INSERT INTO `build_info` VALUES (22410,7,0,3,NULL);
INSERT INTO `build_info` VALUES (22423,7,0,3,NULL);
INSERT INTO `build_info` VALUES (22498,7,0,3,NULL);
INSERT INTO `build_info` VALUES (22522,7,0,3,NULL);
INSERT INTO `build_info` VALUES (22566,7,0,3,NULL);
INSERT INTO `build_info` VALUES (22594,7,0,3,NULL);
INSERT INTO `build_info` VALUES (22624,7,0,3,NULL);
INSERT INTO `build_info` VALUES (22747,7,0,3,NULL);
INSERT INTO `build_info` VALUES (22810,7,0,3,NULL);
INSERT INTO `build_info` VALUES (22900,7,1,0,NULL);
INSERT INTO `build_info` VALUES (22908,7,1,0,NULL);
INSERT INTO `build_info` VALUES (22950,7,1,0,NULL);
INSERT INTO `build_info` VALUES (22995,7,1,0,NULL);
INSERT INTO `build_info` VALUES (22996,7,1,0,NULL);
INSERT INTO `build_info` VALUES (23171,7,1,0,NULL);
INSERT INTO `build_info` VALUES (23222,7,1,0,NULL);
INSERT INTO `build_info` VALUES (23360,7,1,5,NULL);
INSERT INTO `build_info` VALUES (23420,7,1,5,NULL);
INSERT INTO `build_info` VALUES (23911,7,2,0,NULL);
INSERT INTO `build_info` VALUES (23937,7,2,0,NULL);
INSERT INTO `build_info` VALUES (24015,7,2,0,NULL);
INSERT INTO `build_info` VALUES (24330,7,2,5,NULL);
INSERT INTO `build_info` VALUES (24367,7,2,5,NULL);
INSERT INTO `build_info` VALUES (24415,7,2,5,NULL);
INSERT INTO `build_info` VALUES (24430,7,2,5,NULL);
INSERT INTO `build_info` VALUES (24461,7,2,5,NULL);
INSERT INTO `build_info` VALUES (24742,7,2,5,NULL);
INSERT INTO `build_info` VALUES (25549,7,3,2,NULL);
INSERT INTO `build_info` VALUES (25996,7,3,5,NULL);
INSERT INTO `build_info` VALUES (26124,7,3,5,NULL);
INSERT INTO `build_info` VALUES (26365,7,3,5,NULL);
INSERT INTO `build_info` VALUES (26654,7,3,5,NULL);
INSERT INTO `build_info` VALUES (26822,7,3,5,NULL);
INSERT INTO `build_info` VALUES (26899,7,3,5,NULL);
INSERT INTO `build_info` VALUES (26972,7,3,5,NULL);
INSERT INTO `build_info` VALUES (28153,8,0,1,NULL);
INSERT INTO `build_info` VALUES (30706,8,1,5,NULL);
INSERT INTO `build_info` VALUES (30993,8,2,0,NULL);
INSERT INTO `build_info` VALUES (31229,8,2,0,NULL);
INSERT INTO `build_info` VALUES (31429,8,2,0,NULL);
INSERT INTO `build_info` VALUES (31478,8,2,0,NULL);
INSERT INTO `build_info` VALUES (32305,8,2,5,NULL);
INSERT INTO `build_info` VALUES (32494,8,2,5,NULL);
INSERT INTO `build_info` VALUES (32580,8,2,5,NULL);
INSERT INTO `build_info` VALUES (32638,8,2,5,NULL);
INSERT INTO `build_info` VALUES (32722,8,2,5,NULL);
INSERT INTO `build_info` VALUES (32750,8,2,5,NULL);
INSERT INTO `build_info` VALUES (32978,8,2,5,NULL);
INSERT INTO `build_info` VALUES (33369,8,3,0,NULL);
INSERT INTO `build_info` VALUES (33528,8,3,0,NULL);
INSERT INTO `build_info` VALUES (33724,8,3,0,NULL);
INSERT INTO `build_info` VALUES (33775,8,3,0,NULL);
INSERT INTO `build_info` VALUES (33941,8,3,0,NULL);
INSERT INTO `build_info` VALUES (34220,8,3,0,NULL);
INSERT INTO `build_info` VALUES (34601,8,3,0,NULL);
INSERT INTO `build_info` VALUES (34769,8,3,0,NULL);
INSERT INTO `build_info` VALUES (34963,8,3,0,NULL);
INSERT INTO `build_info` VALUES (35249,8,3,7,NULL);
INSERT INTO `build_info` VALUES (35284,8,3,7,NULL);
INSERT INTO `build_info` VALUES (35435,8,3,7,NULL);
INSERT INTO `build_info` VALUES (35662,8,3,7,NULL);
INSERT INTO `build_info` VALUES (36753,9,0,2,NULL);
INSERT INTO `build_info` VALUES (36839,9,0,2,NULL);
INSERT INTO `build_info` VALUES (36949,9,0,2,NULL);
INSERT INTO `build_info` VALUES (37142,9,0,2,NULL);
INSERT INTO `build_info` VALUES (37176,9,0,2,NULL);
INSERT INTO `build_info` VALUES (37474,9,0,2,NULL);
INSERT INTO `build_info` VALUES (38134,9,0,5,NULL);
INSERT INTO `build_info` VALUES (38556,9,0,5,NULL);
INSERT INTO `build_info` VALUES (39653,9,1,0,NULL);
INSERT INTO `build_info` VALUES (39804,9,1,0,NULL);
INSERT INTO `build_info` VALUES (40000,9,1,0,NULL);
INSERT INTO `build_info` VALUES (40120,9,1,0,NULL);
INSERT INTO `build_info` VALUES (40443,9,1,0,NULL);
INSERT INTO `build_info` VALUES (40593,9,1,0,NULL);
INSERT INTO `build_info` VALUES (40725,9,1,0,NULL);
INSERT INTO `build_info` VALUES (40906,9,1,5,NULL);
INSERT INTO `build_info` VALUES (40944,9,1,5,NULL);
INSERT INTO `build_info` VALUES (40966,9,1,5,NULL);
INSERT INTO `build_info` VALUES (41031,9,1,5,NULL);
INSERT INTO `build_info` VALUES (41079,9,1,5,NULL);
INSERT INTO `build_info` VALUES (41288,9,1,5,NULL);
INSERT INTO `build_info` VALUES (41323,9,1,5,NULL);
INSERT INTO `build_info` VALUES (41359,9,1,5,NULL);
INSERT INTO `build_info` VALUES (41488,9,1,5,NULL);
INSERT INTO `build_info` VALUES (41793,9,1,5,NULL);
INSERT INTO `build_info` VALUES (42010,9,1,5,NULL);
INSERT INTO `build_info` VALUES (42423,9,2,0,NULL);
INSERT INTO `build_info` VALUES (42488,9,2,0,NULL);
INSERT INTO `build_info` VALUES (42521,9,2,0,NULL);
INSERT INTO `build_info` VALUES (42538,9,2,0,NULL);
INSERT INTO `build_info` VALUES (42560,9,2,0,NULL);
INSERT INTO `build_info` VALUES (42614,9,2,0,NULL);
INSERT INTO `build_info` VALUES (42698,9,2,0,NULL);
INSERT INTO `build_info` VALUES (42825,9,2,0,NULL);
INSERT INTO `build_info` VALUES (42852,9,2,0,NULL);
INSERT INTO `build_info` VALUES (42937,9,2,0,NULL);
INSERT INTO `build_info` VALUES (42979,9,2,0,NULL);
INSERT INTO `build_info` VALUES (43114,9,2,0,NULL);
INSERT INTO `build_info` VALUES (43206,9,2,0,NULL);
INSERT INTO `build_info` VALUES (43340,9,2,0,NULL);
INSERT INTO `build_info` VALUES (43345,9,2,0,NULL);
INSERT INTO `build_info` VALUES (43971,9,2,5,NULL);
INSERT INTO `build_info` VALUES (44015,9,2,5,NULL);
INSERT INTO `build_info` VALUES (44061,9,2,5,NULL);
INSERT INTO `build_info` VALUES (44127,9,2,5,NULL);
INSERT INTO `build_info` VALUES (44232,9,2,5,NULL);
INSERT INTO `build_info` VALUES (44325,9,2,5,NULL);
INSERT INTO `build_info` VALUES (44730,9,2,5,NULL);
INSERT INTO `build_info` VALUES (44908,9,2,5,NULL);
INSERT INTO `build_info` VALUES (45114,9,2,7,NULL);
INSERT INTO `build_info` VALUES (45161,9,2,7,NULL);
INSERT INTO `build_info` VALUES (45338,9,2,7,NULL);
INSERT INTO `build_info` VALUES (45745,9,2,7,NULL);
INSERT INTO `build_info` VALUES (46479,10,0,2,NULL);
INSERT INTO `build_info` VALUES (46658,10,0,2,NULL);
INSERT INTO `build_info` VALUES (46689,10,0,2,NULL);
INSERT INTO `build_info` VALUES (46702,10,0,2,NULL);
INSERT INTO `build_info` VALUES (46741,10,0,2,NULL);
INSERT INTO `build_info` VALUES (46801,10,0,2,NULL);
INSERT INTO `build_info` VALUES (46879,10,0,2,NULL);
INSERT INTO `build_info` VALUES (46924,10,0,2,NULL);
INSERT INTO `build_info` VALUES (47067,10,0,2,NULL);
INSERT INTO `build_info` VALUES (47187,10,0,2,NULL);
INSERT INTO `build_info` VALUES (47213,10,0,2,NULL);
INSERT INTO `build_info` VALUES (47631,10,0,2,NULL);
INSERT INTO `build_info` VALUES (47777,10,0,5,NULL);
INSERT INTO `build_info` VALUES (47799,10,0,5,NULL);
INSERT INTO `build_info` VALUES (47825,10,0,5,NULL);
INSERT INTO `build_info` VALUES (47849,10,0,5,NULL);
INSERT INTO `build_info` VALUES (47871,10,0,5,NULL);
INSERT INTO `build_info` VALUES (47884,10,0,5,NULL);
INSERT INTO `build_info` VALUES (47936,10,0,5,NULL);
INSERT INTO `build_info` VALUES (47967,10,0,5,NULL);
INSERT INTO `build_info` VALUES (48001,10,0,5,NULL);
INSERT INTO `build_info` VALUES (48069,10,0,5,NULL);
INSERT INTO `build_info` VALUES (48317,10,0,5,NULL);
INSERT INTO `build_info` VALUES (48397,10,0,5,NULL);
INSERT INTO `build_info` VALUES (48526,10,0,5,NULL);
INSERT INTO `build_info` VALUES (48676,10,0,7,NULL);
INSERT INTO `build_info` VALUES (48749,10,0,7,NULL);
INSERT INTO `build_info` VALUES (48838,10,0,7,NULL);
INSERT INTO `build_info` VALUES (48865,10,0,7,NULL);
INSERT INTO `build_info` VALUES (48892,10,0,7,NULL);
INSERT INTO `build_info` VALUES (48966,10,0,7,NULL);
INSERT INTO `build_info` VALUES (48999,10,0,7,NULL);
INSERT INTO `build_info` VALUES (49267,10,0,7,NULL);
INSERT INTO `build_info` VALUES (49318,10,1,0,NULL);
INSERT INTO `build_info` VALUES (49343,10,0,7,NULL);
INSERT INTO `build_info` VALUES (49407,10,1,0,NULL);
INSERT INTO `build_info` VALUES (49426,10,1,0,NULL);
INSERT INTO `build_info` VALUES (49444,10,1,0,NULL);
INSERT INTO `build_info` VALUES (49474,10,1,0,NULL);
INSERT INTO `build_info` VALUES (49570,10,1,0,NULL);
INSERT INTO `build_info` VALUES (49679,10,1,0,NULL);
INSERT INTO `build_info` VALUES (49741,10,1,0,NULL);
INSERT INTO `build_info` VALUES (49801,10,1,0,NULL);
INSERT INTO `build_info` VALUES (49890,10,1,0,NULL);
INSERT INTO `build_info` VALUES (50000,10,1,0,NULL);
INSERT INTO `build_info` VALUES (50401,10,1,5,NULL);
INSERT INTO `build_info` VALUES (50438,10,1,5,NULL);
INSERT INTO `build_info` VALUES (50467,10,1,5,NULL);
INSERT INTO `build_info` VALUES (50469,10,1,5,NULL);
INSERT INTO `build_info` VALUES (50504,10,1,5,NULL);
INSERT INTO `build_info` VALUES (50585,10,1,5,NULL);
INSERT INTO `build_info` VALUES (50622,10,1,5,NULL);
INSERT INTO `build_info` VALUES (50747,10,1,5,NULL);
INSERT INTO `build_info` VALUES (50791,10,1,5,NULL);
INSERT INTO `build_info` VALUES (51130,10,1,5,NULL);
INSERT INTO `build_info` VALUES (51187,10,1,7,NULL);
INSERT INTO `build_info` VALUES (51237,10,1,7,NULL);
INSERT INTO `build_info` VALUES (51261,10,1,7,NULL);
INSERT INTO `build_info` VALUES (51313,10,1,7,NULL);
INSERT INTO `build_info` VALUES (51421,10,1,7,NULL);
INSERT INTO `build_info` VALUES (51485,10,1,7,NULL);
INSERT INTO `build_info` VALUES (51536,10,1,7,NULL);
INSERT INTO `build_info` VALUES (51754,10,1,7,NULL);
INSERT INTO `build_info` VALUES (51886,10,1,7,NULL);
INSERT INTO `build_info` VALUES (51972,10,1,7,NULL);
INSERT INTO `build_info` VALUES (52038,10,2,0,NULL);
INSERT INTO `build_info` VALUES (52068,10,2,0,NULL);
INSERT INTO `build_info` VALUES (52095,10,2,0,NULL);
INSERT INTO `build_info` VALUES (52106,10,2,0,NULL);
INSERT INTO `build_info` VALUES (52129,10,2,0,NULL);
INSERT INTO `build_info` VALUES (52148,10,2,0,NULL);
INSERT INTO `build_info` VALUES (52188,10,2,0,NULL);
INSERT INTO `build_info` VALUES (52301,10,2,0,NULL);
INSERT INTO `build_info` VALUES (52393,10,2,0,NULL);
INSERT INTO `build_info` VALUES (52485,10,2,0,NULL);
INSERT INTO `build_info` VALUES (52545,10,2,0,NULL);
INSERT INTO `build_info` VALUES (52607,10,2,0,NULL);
INSERT INTO `build_info` VALUES (52649,10,2,0,NULL);
INSERT INTO `build_info` VALUES (52808,10,2,0,NULL);
INSERT INTO `build_info` VALUES (52902,10,2,5,NULL);
INSERT INTO `build_info` VALUES (52968,10,2,5,NULL);
INSERT INTO `build_info` VALUES (52983,10,2,5,NULL);
INSERT INTO `build_info` VALUES (53007,10,2,5,NULL);
INSERT INTO `build_info` VALUES (53040,10,2,5,NULL);
INSERT INTO `build_info` VALUES (53104,10,2,5,NULL);
INSERT INTO `build_info` VALUES (53162,10,2,5,NULL);
INSERT INTO `build_info` VALUES (53212,10,2,5,NULL);
INSERT INTO `build_info` VALUES (53262,10,2,5,NULL);
INSERT INTO `build_info` VALUES (53441,10,2,5,NULL);
INSERT INTO `build_info` VALUES (53584,10,2,5,NULL);
INSERT INTO `build_info` VALUES (53840,10,2,6,NULL);
INSERT INTO `build_info` VALUES (53877,10,2,6,NULL);
INSERT INTO `build_info` VALUES (53913,10,2,6,NULL);
INSERT INTO `build_info` VALUES (53989,10,2,6,NULL);
INSERT INTO `build_info` VALUES (54070,10,2,6,NULL);
INSERT INTO `build_info` VALUES (54205,10,2,6,NULL);
INSERT INTO `build_info` VALUES (54358,10,2,6,NULL);
INSERT INTO `build_info` VALUES (54499,10,2,6,NULL);
INSERT INTO `build_info` VALUES (54577,10,2,7,NULL);
INSERT INTO `build_info` VALUES (54601,10,2,7,NULL);
INSERT INTO `build_info` VALUES (54604,10,2,7,NULL);
INSERT INTO `build_info` VALUES (54630,10,2,7,NULL);
INSERT INTO `build_info` VALUES (54673,10,2,7,NULL);
INSERT INTO `build_info` VALUES (54717,10,2,7,NULL);
INSERT INTO `build_info` VALUES (54736,10,2,7,NULL);
INSERT INTO `build_info` VALUES (54762,10,2,7,NULL);
INSERT INTO `build_info` VALUES (54847,10,2,7,NULL);
INSERT INTO `build_info` VALUES (54904,10,2,7,NULL);
INSERT INTO `build_info` VALUES (54988,10,2,7,NULL);
INSERT INTO `build_info` VALUES (55142,10,2,7,NULL);
INSERT INTO `build_info` VALUES (55165,10,2,7,NULL);
INSERT INTO `build_info` VALUES (55261,10,2,7,NULL);
INSERT INTO `build_info` VALUES (55461,10,2,7,NULL);
INSERT INTO `build_info` VALUES (55664,10,2,7,NULL);
INSERT INTO `build_info` VALUES (55666,11,0,0,NULL);
INSERT INTO `build_info` VALUES (55792,11,0,0,NULL);
INSERT INTO `build_info` VALUES (55793,11,0,0,NULL);
INSERT INTO `build_info` VALUES (55818,11,0,0,NULL);
INSERT INTO `build_info` VALUES (55824,11,0,0,NULL);
INSERT INTO `build_info` VALUES (55846,11,0,0,NULL);
INSERT INTO `build_info` VALUES (55933,11,0,0,NULL);
INSERT INTO `build_info` VALUES (55939,11,0,0,NULL);
INSERT INTO `build_info` VALUES (55959,11,0,2,NULL);
INSERT INTO `build_info` VALUES (55960,11,0,0,NULL);
INSERT INTO `build_info` VALUES (56008,11,0,0,NULL);
INSERT INTO `build_info` VALUES (56110,11,0,2,NULL);
INSERT INTO `build_info` VALUES (56162,11,0,2,NULL);
INSERT INTO `build_info` VALUES (56196,11,0,2,NULL);
INSERT INTO `build_info` VALUES (56263,11,0,2,NULL);
INSERT INTO `build_info` VALUES (56288,11,0,2,NULL);
INSERT INTO `build_info` VALUES (56311,11,0,2,NULL);
INSERT INTO `build_info` VALUES (56313,11,0,2,NULL);
INSERT INTO `build_info` VALUES (56380,11,0,2,NULL);
INSERT INTO `build_info` VALUES (56382,11,0,2,NULL);
INSERT INTO `build_info` VALUES (56421,11,0,2,NULL);
INSERT INTO `build_info` VALUES (56461,11,0,2,NULL);
INSERT INTO `build_info` VALUES (56513,11,0,2,NULL);
INSERT INTO `build_info` VALUES (56625,11,0,2,NULL);
INSERT INTO `build_info` VALUES (56647,11,0,2,NULL);
INSERT INTO `build_info` VALUES (56819,11,0,2,NULL);
INSERT INTO `build_info` VALUES (57171,11,0,5,NULL);
INSERT INTO `build_info` VALUES (57212,11,0,5,NULL);
INSERT INTO `build_info` VALUES (57292,11,0,5,NULL);
INSERT INTO `build_info` VALUES (57388,11,0,5,NULL);
INSERT INTO `build_info` VALUES (57534,11,0,5,NULL);
INSERT INTO `build_info` VALUES (57637,11,0,5,NULL);
INSERT INTO `build_info` VALUES (57689,11,0,5,NULL);
INSERT INTO `build_info` VALUES (58123,11,0,7,NULL);
INSERT INTO `build_info` VALUES (58162,11,0,7,NULL);
INSERT INTO `build_info` VALUES (58187,11,0,7,NULL);
INSERT INTO `build_info` VALUES (58238,11,0,7,NULL);
INSERT INTO `build_info` VALUES (58533,11,0,7,NULL);
INSERT INTO `build_info` VALUES (58608,11,0,7,NULL);
INSERT INTO `build_info` VALUES (58630,11,0,7,NULL);
INSERT INTO `build_info` VALUES (58680,11,0,7,NULL);
INSERT INTO `build_info` VALUES (58773,11,0,7,NULL);
INSERT INTO `build_info` VALUES (58867,11,0,7,NULL);
INSERT INTO `build_info` VALUES (58911,11,0,7,NULL);
INSERT INTO `build_info` VALUES (59207,11,0,7,NULL);
INSERT INTO `build_info` VALUES (59302,11,0,7,NULL);
INSERT INTO `build_info` VALUES (59347,11,1,0,NULL);
INSERT INTO `build_info` VALUES (59425,11,1,0,NULL);
INSERT INTO `build_info` VALUES (59466,11,1,0,NULL);
INSERT INTO `build_info` VALUES (59538,11,1,0,NULL);
INSERT INTO `build_info` VALUES (59570,11,1,0,NULL);
INSERT INTO `build_info` VALUES (59679,11,1,0,NULL);
INSERT INTO `build_info` VALUES (59888,11,1,0,NULL);
INSERT INTO `build_info` VALUES (60037,11,1,0,NULL);
INSERT INTO `build_info` VALUES (60189,11,1,0,NULL);
INSERT INTO `build_info` VALUES (60228,11,1,0,NULL);
INSERT INTO `build_info` VALUES (60257,11,1,0,NULL);
INSERT INTO `build_info` VALUES (60392,11,1,5,NULL);
INSERT INTO `build_info` VALUES (60428,11,1,5,NULL);
INSERT INTO `build_info` VALUES (60490,11,1,5,NULL);
INSERT INTO `build_info` VALUES (60568,11,1,5,NULL);
INSERT INTO `build_info` VALUES (60822,11,1,5,NULL);
INSERT INTO `build_info` VALUES (61122,11,1,5,NULL);
INSERT INTO `build_info` VALUES (61188,11,1,5,NULL);
INSERT INTO `build_info` VALUES (61265,11,1,5,NULL);
INSERT INTO `build_info` VALUES (61491,11,1,7,NULL);
INSERT INTO `build_info` VALUES (61559,11,1,7,NULL);
INSERT INTO `build_info` VALUES (61609,11,1,7,NULL);
INSERT INTO `build_info` VALUES (61967,11,1,7,NULL);
INSERT INTO `build_info` VALUES (62213,11,2,0,NULL);
INSERT INTO `build_info` VALUES (62417,11,2,0,NULL);
INSERT INTO `build_info` VALUES (62438,11,2,0,NULL);
INSERT INTO `build_info` VALUES (62493,11,2,0,NULL);
INSERT INTO `build_info` VALUES (62680,11,2,0,NULL);
INSERT INTO `build_info` VALUES (62706,11,2,0,NULL);
INSERT INTO `build_info` VALUES (62748,11,2,0,NULL);
INSERT INTO `build_info` VALUES (62801,11,2,0,NULL);
INSERT INTO `build_info` VALUES (62876,11,2,0,NULL);
INSERT INTO `build_info` VALUES (62958,11,2,0,NULL);
INSERT INTO `build_info` VALUES (63003,11,2,0,NULL);
INSERT INTO `build_info` VALUES (63163,11,2,0,NULL);
INSERT INTO `build_info` VALUES (63305,11,2,0,NULL);
INSERT INTO `build_info` VALUES (63506,11,2,5,NULL);
INSERT INTO `build_info` VALUES (63660,11,2,5,NULL);
INSERT INTO `build_info` VALUES (63704,11,2,5,NULL);
INSERT INTO `build_info` VALUES (63796,11,2,5,NULL);
INSERT INTO `build_info` VALUES (63834,11,2,5,NULL);
INSERT INTO `build_info` VALUES (63906,11,2,5,NULL);
INSERT INTO `build_info` VALUES (64154,11,2,5,NULL);
INSERT INTO `build_info` VALUES (64270,11,2,5,NULL);
INSERT INTO `build_info` VALUES (64395,11,2,5,NULL);
INSERT INTO `build_info` VALUES (64484,11,2,5,NULL);
INSERT INTO `build_info` VALUES (64502,11,2,5,NULL);
INSERT INTO `build_info` VALUES (64877,11,2,7,NULL);
INSERT INTO `build_info` VALUES (64978,11,2,7,NULL);
INSERT INTO `build_info` VALUES (65299,11,2,7,NULL);
INSERT INTO `build_info` VALUES (65560,12,0,0,NULL);
INSERT INTO `build_info` VALUES (65655,12,0,0,NULL);
INSERT INTO `build_info` VALUES (65699,12,0,0,NULL);
INSERT INTO `build_info` VALUES (65727,12,0,0,NULL);
INSERT INTO `build_info` VALUES (65818,12,0,1,NULL);
INSERT INTO `build_info` VALUES (65848,12,0,1,NULL);
INSERT INTO `build_info` VALUES (65867,12,0,1,NULL);
INSERT INTO `build_info` VALUES (65893,12,0,1,NULL);
INSERT INTO `build_info` VALUES (65940,12,0,1,NULL);
INSERT INTO `build_info` VALUES (66017,12,0,1,NULL);
INSERT INTO `build_info` VALUES (66044,12,0,1,NULL);
INSERT INTO `build_info` VALUES (66066,12,0,1,NULL);
INSERT INTO `build_info` VALUES (66102,12,0,1,NULL);
INSERT INTO `build_info` VALUES (66192,12,0,1,NULL);
INSERT INTO `build_info` VALUES (66198,12,0,1,NULL);
INSERT INTO `build_info` VALUES (66220,12,0,1,NULL);
INSERT INTO `build_info` VALUES (66263,12,0,1,NULL);
INSERT INTO `build_info` VALUES (66337,12,0,1,NULL);
INSERT INTO `build_info` VALUES (66384,12,0,1,NULL);
INSERT INTO `build_info` VALUES (66431,12,0,1,NULL);
INSERT INTO `build_info` VALUES (66527,12,0,1,NULL);
INSERT INTO `build_info` VALUES (66562,12,0,1,NULL);
INSERT INTO `build_info` VALUES (66666,12,0,1,NULL);
INSERT INTO `build_info` VALUES (66709,12,0,1,NULL);
INSERT INTO `build_info` VALUES (66838,12,0,1,NULL);
INSERT INTO `build_info` VALUES (67088,12,0,5,NULL);
INSERT INTO `build_info` VALUES (67114,12,0,5,NULL);
INSERT INTO `build_info` VALUES (67186,12,0,5,NULL);
INSERT INTO `build_info` VALUES (67235,12,0,5,NULL);
INSERT INTO `build_info` VALUES (67314,12,0,5,NULL);
INSERT INTO `build_info` VALUES (67403,12,0,5,NULL);
INSERT INTO `build_info` VALUES (67451,12,0,5,NULL);
INSERT INTO `build_info` VALUES (67602,12,0,5,NULL);
INSERT INTO `build_info` VALUES (67823,12,0,5,NULL);
INSERT INTO `build_info` VALUES (68182,12,0,7,NULL);
INSERT INTO `build_info` VALUES (68232,12,0,7,NULL);
INSERT INTO `build_info` VALUES (68235,12,0,7,NULL);
INSERT INTO `build_info` VALUES (68256,12,0,7,NULL);
INSERT INTO `build_info` VALUES (68275,12,0,7,NULL);
INSERT INTO `build_info` VALUES (68367,12,0,7,NULL);
INSERT INTO `build_info` VALUES (68453,12,0,7,NULL);
INSERT INTO `build_info` VALUES (68887,12,0,7,NULL);
INSERT INTO `build_info` VALUES (68974,12,0,7,NULL);
INSERT INTO `build_info` VALUES (69404,12,1,0,NULL);
INSERT INTO `build_info` VALUES (69465,12,1,0,NULL);
INSERT INTO `build_info` VALUES (69497,12,1,0,NULL);
INSERT INTO `build_info` VALUES (69587,12,1,0,NULL);
INSERT INTO `build_info` VALUES (69795,2,5,6,NULL);
INSERT INTO `build_info` VALUES (69814,12,1,0,NULL);
INSERT INTO `build_info` VALUES (69875,12,1,0,NULL);
INSERT INTO `build_info` VALUES (69933,12,1,0,NULL);
INSERT INTO `build_info` VALUES (70009,1,60,1,NULL);
INSERT INTO `build_info` VALUES (70058,1,60,1,NULL);
INSERT INTO `build_info` VALUES (70124,1,60,1,NULL);
INSERT INTO `build_info` VALUES (70170,1,60,1,NULL);
INSERT INTO `build_info` VALUES (70205,1,60,1,NULL);
INSERT INTO `build_info` VALUES (70235,1,60,1,NULL);
INSERT INTO `build_info` VALUES (70245,1,60,1,NULL);
INSERT INTO `build_info` VALUES (70291,1,60,1,NULL);
INSERT INTO `build_info` VALUES (70338,1,60,1,NULL);
INSERT INTO `build_info` VALUES (70425,1,60,1,NULL);
/*!40000 ALTER TABLE `build_info` ENABLE KEYS */;

/*!40000 ALTER TABLE `rbac_permissions` DISABLE KEYS */;
INSERT INTO `rbac_permissions` VALUES (1,'Instant logout');
INSERT INTO `rbac_permissions` VALUES (2,'Skip Queue');
INSERT INTO `rbac_permissions` VALUES (3,'Join Normal Battleground');
INSERT INTO `rbac_permissions` VALUES (4,'Join Random Battleground');
INSERT INTO `rbac_permissions` VALUES (5,'Join Arenas');
INSERT INTO `rbac_permissions` VALUES (6,'Join Dungeon Finder');
INSERT INTO `rbac_permissions` VALUES (7,'Skip idle connection check');
INSERT INTO `rbac_permissions` VALUES (8,'Cannot earn achievements');
INSERT INTO `rbac_permissions` VALUES (9,'Cannot earn realm first achievements');
INSERT INTO `rbac_permissions` VALUES (10,'Use character templates');
INSERT INTO `rbac_permissions` VALUES (11,'Log GM trades');
INSERT INTO `rbac_permissions` VALUES (12,'Skip character creation demon hunter min level check');
INSERT INTO `rbac_permissions` VALUES (13,'Skip Instance required bosses check');
INSERT INTO `rbac_permissions` VALUES (14,'Skip character creation team mask check');
INSERT INTO `rbac_permissions` VALUES (15,'Skip character creation class mask check');
INSERT INTO `rbac_permissions` VALUES (16,'Skip character creation race mask check');
INSERT INTO `rbac_permissions` VALUES (17,'Skip character creation reserved name check');
INSERT INTO `rbac_permissions` VALUES (18,'Skip character creation death knight min level check');
INSERT INTO `rbac_permissions` VALUES (19,'Skip needed requirements to use channel check');
INSERT INTO `rbac_permissions` VALUES (20,'Skip disable map check');
INSERT INTO `rbac_permissions` VALUES (21,'Skip reset talents when used more than allowed check');
INSERT INTO `rbac_permissions` VALUES (22,'Skip spam chat check');
INSERT INTO `rbac_permissions` VALUES (23,'Skip over-speed ping check');
INSERT INTO `rbac_permissions` VALUES (24,'Two side faction characters on the same account');
INSERT INTO `rbac_permissions` VALUES (25,'Allow say chat between factions');
INSERT INTO `rbac_permissions` VALUES (26,'Allow channel chat between factions');
INSERT INTO `rbac_permissions` VALUES (27,'Two side mail interaction');
INSERT INTO `rbac_permissions` VALUES (28,'See two side who list');
INSERT INTO `rbac_permissions` VALUES (29,'Add friends of other faction');
INSERT INTO `rbac_permissions` VALUES (30,'Save character without delay with .save command');
INSERT INTO `rbac_permissions` VALUES (31,'Use params with .unstuck command');
INSERT INTO `rbac_permissions` VALUES (32,'Can be assigned tickets with .assign ticket command');
INSERT INTO `rbac_permissions` VALUES (33,'Notify if a command was not found');
INSERT INTO `rbac_permissions` VALUES (34,'Check if should appear in list using .gm ingame command');
INSERT INTO `rbac_permissions` VALUES (35,'See all security levels with who command');
INSERT INTO `rbac_permissions` VALUES (36,'Filter whispers');
INSERT INTO `rbac_permissions` VALUES (37,'Use staff badge in chat');
INSERT INTO `rbac_permissions` VALUES (38,'Resurrect with full Health Points');
INSERT INTO `rbac_permissions` VALUES (39,'Restore saved gm setting states');
INSERT INTO `rbac_permissions` VALUES (40,'Allows to add a gm to friend list');
INSERT INTO `rbac_permissions` VALUES (41,'Use Config option START_GM_LEVEL to assign new character level');
INSERT INTO `rbac_permissions` VALUES (42,'Allows to use CMSG_WORLD_TELEPORT opcode');
INSERT INTO `rbac_permissions` VALUES (43,'Allows to use CMSG_WHOIS opcode');
INSERT INTO `rbac_permissions` VALUES (44,'Receive global GM messages/texts');
INSERT INTO `rbac_permissions` VALUES (45,'Join channels without announce');
INSERT INTO `rbac_permissions` VALUES (46,'Change channel settings without being channel moderator');
INSERT INTO `rbac_permissions` VALUES (47,'Can ignore non-strong lower security checks if it\'s disabled in config');
INSERT INTO `rbac_permissions` VALUES (48,'Enable IP, Last Login and EMail output in pinfo');
INSERT INTO `rbac_permissions` VALUES (49,'Forces to enter the email for confirmation on password change');
INSERT INTO `rbac_permissions` VALUES (50,'Allow user to check his own email with .account');
INSERT INTO `rbac_permissions` VALUES (51,'Allow trading between factions');
INSERT INTO `rbac_permissions` VALUES (52,'No battleground deserter debuff');
INSERT INTO `rbac_permissions` VALUES (53,'Can be AFK on the battleground');
INSERT INTO `rbac_permissions` VALUES (192,'Role: Sec Level Administrator');
INSERT INTO `rbac_permissions` VALUES (193,'Role: Sec Level Gamemaster');
INSERT INTO `rbac_permissions` VALUES (194,'Role: Sec Level Moderator');
INSERT INTO `rbac_permissions` VALUES (195,'Role: Sec Level Player');
INSERT INTO `rbac_permissions` VALUES (196,'Role: Administrator Commands');
INSERT INTO `rbac_permissions` VALUES (197,'Role: Gamemaster Commands');
INSERT INTO `rbac_permissions` VALUES (198,'Role: Moderator Commands');
INSERT INTO `rbac_permissions` VALUES (199,'Role: Player Commands');
INSERT INTO `rbac_permissions` VALUES (200,'Command: rbac');
INSERT INTO `rbac_permissions` VALUES (201,'Command: rbac account');
INSERT INTO `rbac_permissions` VALUES (202,'Command: rbac account list');
INSERT INTO `rbac_permissions` VALUES (203,'Command: rbac account grant');
INSERT INTO `rbac_permissions` VALUES (204,'Command: rbac account deny');
INSERT INTO `rbac_permissions` VALUES (205,'Command: rbac account revoke');
INSERT INTO `rbac_permissions` VALUES (206,'Command: rbac list');
INSERT INTO `rbac_permissions` VALUES (207,'Command: battlenetaccount');
INSERT INTO `rbac_permissions` VALUES (208,'Command: battlenetaccount create');
INSERT INTO `rbac_permissions` VALUES (209,'Command: battlenetaccount lock country');
INSERT INTO `rbac_permissions` VALUES (210,'Command: battlenetaccount lock ip');
INSERT INTO `rbac_permissions` VALUES (211,'Command: battlenetaccount password');
INSERT INTO `rbac_permissions` VALUES (212,'Command: battlenetaccount set');
INSERT INTO `rbac_permissions` VALUES (213,'Command: battlenetaccount set password');
INSERT INTO `rbac_permissions` VALUES (214,'Command: bnetaccount link');
INSERT INTO `rbac_permissions` VALUES (215,'Command: bnetaccount unlink');
INSERT INTO `rbac_permissions` VALUES (216,'Command: bnetaccount gameaccountcreate');
INSERT INTO `rbac_permissions` VALUES (217,'Command: account');
INSERT INTO `rbac_permissions` VALUES (218,'Command: account addon');
INSERT INTO `rbac_permissions` VALUES (219,'Command: account create');
INSERT INTO `rbac_permissions` VALUES (220,'Command: account delete');
INSERT INTO `rbac_permissions` VALUES (221,'Command: account lock');
INSERT INTO `rbac_permissions` VALUES (222,'Command: account lock country');
INSERT INTO `rbac_permissions` VALUES (223,'Command: account lock ip');
INSERT INTO `rbac_permissions` VALUES (224,'Command: account onlinelist');
INSERT INTO `rbac_permissions` VALUES (225,'Command: account password');
INSERT INTO `rbac_permissions` VALUES (226,'Command: account set');
INSERT INTO `rbac_permissions` VALUES (227,'Command: account set addon');
INSERT INTO `rbac_permissions` VALUES (228,'Command: account set gmlevel');
INSERT INTO `rbac_permissions` VALUES (229,'Command: account set password');
INSERT INTO `rbac_permissions` VALUES (230,'Command: achievement');
INSERT INTO `rbac_permissions` VALUES (231,'Command: achievement add');
INSERT INTO `rbac_permissions` VALUES (232,'Command: arena');
INSERT INTO `rbac_permissions` VALUES (233,'Command: arena captain');
INSERT INTO `rbac_permissions` VALUES (234,'Command: arena create');
INSERT INTO `rbac_permissions` VALUES (235,'Command: arena disband');
INSERT INTO `rbac_permissions` VALUES (236,'Command: arena info');
INSERT INTO `rbac_permissions` VALUES (237,'Command: arena lookup');
INSERT INTO `rbac_permissions` VALUES (238,'Command: arena rename');
INSERT INTO `rbac_permissions` VALUES (239,'Command: ban');
INSERT INTO `rbac_permissions` VALUES (240,'Command: ban account');
INSERT INTO `rbac_permissions` VALUES (241,'Command: ban character');
INSERT INTO `rbac_permissions` VALUES (242,'Command: ban ip');
INSERT INTO `rbac_permissions` VALUES (243,'Command: ban playeraccount');
INSERT INTO `rbac_permissions` VALUES (244,'Command: baninfo');
INSERT INTO `rbac_permissions` VALUES (245,'Command: baninfo account');
INSERT INTO `rbac_permissions` VALUES (246,'Command: baninfo character');
INSERT INTO `rbac_permissions` VALUES (247,'Command: baninfo ip');
INSERT INTO `rbac_permissions` VALUES (248,'Command: banlist');
INSERT INTO `rbac_permissions` VALUES (249,'Command: banlist account');
INSERT INTO `rbac_permissions` VALUES (250,'Command: banlist character');
INSERT INTO `rbac_permissions` VALUES (251,'Command: banlist ip');
INSERT INTO `rbac_permissions` VALUES (252,'Command: unban');
INSERT INTO `rbac_permissions` VALUES (253,'Command: unban account');
INSERT INTO `rbac_permissions` VALUES (254,'Command: unban character');
INSERT INTO `rbac_permissions` VALUES (255,'Command: unban ip');
INSERT INTO `rbac_permissions` VALUES (256,'Command: unban playeraccount');
INSERT INTO `rbac_permissions` VALUES (257,'Command: bf');
INSERT INTO `rbac_permissions` VALUES (258,'Command: bf start');
INSERT INTO `rbac_permissions` VALUES (259,'Command: bf stop');
INSERT INTO `rbac_permissions` VALUES (260,'Command: bf switch');
INSERT INTO `rbac_permissions` VALUES (261,'Command: bf timer');
INSERT INTO `rbac_permissions` VALUES (262,'Command: bf enable');
INSERT INTO `rbac_permissions` VALUES (263,'Command: account email');
INSERT INTO `rbac_permissions` VALUES (264,'Command: account set sec');
INSERT INTO `rbac_permissions` VALUES (265,'Command: account set sec email');
INSERT INTO `rbac_permissions` VALUES (266,'Command: account set sec regmail');
INSERT INTO `rbac_permissions` VALUES (267,'Command: cast');
INSERT INTO `rbac_permissions` VALUES (268,'Command: cast back');
INSERT INTO `rbac_permissions` VALUES (269,'Command: cast dist');
INSERT INTO `rbac_permissions` VALUES (270,'Command: cast self');
INSERT INTO `rbac_permissions` VALUES (271,'Command: cast target');
INSERT INTO `rbac_permissions` VALUES (272,'Command: cast dest');
INSERT INTO `rbac_permissions` VALUES (273,'Command: character');
INSERT INTO `rbac_permissions` VALUES (274,'Command: character customize');
INSERT INTO `rbac_permissions` VALUES (275,'Command: character changefaction');
INSERT INTO `rbac_permissions` VALUES (276,'Command: character changerace');
INSERT INTO `rbac_permissions` VALUES (277,'Command: character deleted');
INSERT INTO `rbac_permissions` VALUES (279,'Command: character deleted list');
INSERT INTO `rbac_permissions` VALUES (280,'Command: character deleted restore');
INSERT INTO `rbac_permissions` VALUES (283,'Command: character level');
INSERT INTO `rbac_permissions` VALUES (284,'Command: character rename');
INSERT INTO `rbac_permissions` VALUES (285,'Command: character reputation');
INSERT INTO `rbac_permissions` VALUES (286,'Command: character titles');
INSERT INTO `rbac_permissions` VALUES (287,'Command: levelup');
INSERT INTO `rbac_permissions` VALUES (288,'Command: pdump');
INSERT INTO `rbac_permissions` VALUES (289,'Command: pdump load');
INSERT INTO `rbac_permissions` VALUES (290,'Command: pdump write');
INSERT INTO `rbac_permissions` VALUES (291,'Command: cheat');
INSERT INTO `rbac_permissions` VALUES (292,'Command: cheat casttime');
INSERT INTO `rbac_permissions` VALUES (293,'Command: cheat cooldown');
INSERT INTO `rbac_permissions` VALUES (294,'Command: cheat explore');
INSERT INTO `rbac_permissions` VALUES (295,'Command: cheat god');
INSERT INTO `rbac_permissions` VALUES (296,'Command: cheat power');
INSERT INTO `rbac_permissions` VALUES (297,'Command: cheat status');
INSERT INTO `rbac_permissions` VALUES (298,'Command: cheat taxi');
INSERT INTO `rbac_permissions` VALUES (299,'Command: cheat waterwalk');
INSERT INTO `rbac_permissions` VALUES (300,'Command: debug');
INSERT INTO `rbac_permissions` VALUES (301,'Command: debug anim');
INSERT INTO `rbac_permissions` VALUES (302,'Command: debug areatriggers');
INSERT INTO `rbac_permissions` VALUES (303,'Command: debug arena');
INSERT INTO `rbac_permissions` VALUES (304,'Command: debug bg');
INSERT INTO `rbac_permissions` VALUES (305,'Command: debug entervehicle');
INSERT INTO `rbac_permissions` VALUES (306,'Command: debug getitemstate');
INSERT INTO `rbac_permissions` VALUES (309,'Command: debug combat');
INSERT INTO `rbac_permissions` VALUES (310,'Command: debug itemexpire');
INSERT INTO `rbac_permissions` VALUES (311,'Command: debug lootrecipient');
INSERT INTO `rbac_permissions` VALUES (312,'Command: debug los');
INSERT INTO `rbac_permissions` VALUES (314,'Command: debug moveflags');
INSERT INTO `rbac_permissions` VALUES (315,'Command: debug play');
INSERT INTO `rbac_permissions` VALUES (316,'Command: debug play cinematics');
INSERT INTO `rbac_permissions` VALUES (317,'Command: debug play movie');
INSERT INTO `rbac_permissions` VALUES (318,'Command: debug play sound');
INSERT INTO `rbac_permissions` VALUES (319,'Command: debug send');
INSERT INTO `rbac_permissions` VALUES (320,'Command: debug send buyerror');
INSERT INTO `rbac_permissions` VALUES (321,'Command: debug send channelnotify');
INSERT INTO `rbac_permissions` VALUES (322,'Command: debug send chatmessage');
INSERT INTO `rbac_permissions` VALUES (323,'Command: debug send equiperror');
INSERT INTO `rbac_permissions` VALUES (324,'Command: debug send largepacket');
INSERT INTO `rbac_permissions` VALUES (325,'Command: debug send opcode');
INSERT INTO `rbac_permissions` VALUES (326,'Command: debug send qinvalidmsg');
INSERT INTO `rbac_permissions` VALUES (327,'Command: debug send qpartymsg');
INSERT INTO `rbac_permissions` VALUES (328,'Command: debug send sellerror');
INSERT INTO `rbac_permissions` VALUES (329,'Command: debug send setphaseshift');
INSERT INTO `rbac_permissions` VALUES (330,'Command: debug send spellfail');
INSERT INTO `rbac_permissions` VALUES (331,'Command: debug setaurastate');
INSERT INTO `rbac_permissions` VALUES (335,'Command: debug setvid');
INSERT INTO `rbac_permissions` VALUES (336,'Command: debug spawnvehicle');
INSERT INTO `rbac_permissions` VALUES (337,'Command: debug threat');
INSERT INTO `rbac_permissions` VALUES (339,'Command: debug worldstate');
INSERT INTO `rbac_permissions` VALUES (340,'Command: wpgps');
INSERT INTO `rbac_permissions` VALUES (341,'Command: deserter');
INSERT INTO `rbac_permissions` VALUES (342,'Command: deserter bg');
INSERT INTO `rbac_permissions` VALUES (343,'Command: deserter bg add');
INSERT INTO `rbac_permissions` VALUES (344,'Command: deserter bg remove');
INSERT INTO `rbac_permissions` VALUES (345,'Command: deserter instance');
INSERT INTO `rbac_permissions` VALUES (346,'Command: deserter instance add');
INSERT INTO `rbac_permissions` VALUES (347,'Command: deserter instance remove');
INSERT INTO `rbac_permissions` VALUES (348,'Command: disable');
INSERT INTO `rbac_permissions` VALUES (349,'Command: disable add');
INSERT INTO `rbac_permissions` VALUES (350,'Command: disable add criteria');
INSERT INTO `rbac_permissions` VALUES (351,'Command: disable add battleground');
INSERT INTO `rbac_permissions` VALUES (352,'Command: disable add map');
INSERT INTO `rbac_permissions` VALUES (353,'Command: disable add mmap');
INSERT INTO `rbac_permissions` VALUES (354,'Command: disable add outdoorpvp');
INSERT INTO `rbac_permissions` VALUES (355,'Command: disable add quest');
INSERT INTO `rbac_permissions` VALUES (356,'Command: disable add spell');
INSERT INTO `rbac_permissions` VALUES (357,'Command: disable add vmap');
INSERT INTO `rbac_permissions` VALUES (358,'Command: disable remove');
INSERT INTO `rbac_permissions` VALUES (359,'Command: disable remove criteria');
INSERT INTO `rbac_permissions` VALUES (360,'Command: disable remove battleground');
INSERT INTO `rbac_permissions` VALUES (361,'Command: disable remove map');
INSERT INTO `rbac_permissions` VALUES (362,'Command: disable remove mmap');
INSERT INTO `rbac_permissions` VALUES (363,'Command: disable remove outdoorpvp');
INSERT INTO `rbac_permissions` VALUES (364,'Command: disable remove quest');
INSERT INTO `rbac_permissions` VALUES (365,'Command: disable remove spell');
INSERT INTO `rbac_permissions` VALUES (366,'Command: disable remove vmap');
INSERT INTO `rbac_permissions` VALUES (367,'Command: event info');
INSERT INTO `rbac_permissions` VALUES (368,'Command: event activelist');
INSERT INTO `rbac_permissions` VALUES (369,'Command: event start');
INSERT INTO `rbac_permissions` VALUES (370,'Command: event stop');
INSERT INTO `rbac_permissions` VALUES (371,'Command: gm');
INSERT INTO `rbac_permissions` VALUES (372,'Command: gm chat');
INSERT INTO `rbac_permissions` VALUES (373,'Command: gm fly');
INSERT INTO `rbac_permissions` VALUES (374,'Command: gm ingame');
INSERT INTO `rbac_permissions` VALUES (375,'Command: gm list');
INSERT INTO `rbac_permissions` VALUES (376,'Command: gm visible');
INSERT INTO `rbac_permissions` VALUES (377,'Command: go');
INSERT INTO `rbac_permissions` VALUES (378,'Command: account 2fa');
INSERT INTO `rbac_permissions` VALUES (379,'Command: account 2fa setup');
INSERT INTO `rbac_permissions` VALUES (380,'Command: account 2fa remove');
INSERT INTO `rbac_permissions` VALUES (381,'Command: account set 2fa');
INSERT INTO `rbac_permissions` VALUES (387,'Command: gobject');
INSERT INTO `rbac_permissions` VALUES (388,'Command: gobject activate');
INSERT INTO `rbac_permissions` VALUES (389,'Command: gobject add');
INSERT INTO `rbac_permissions` VALUES (390,'Command: gobject add temp');
INSERT INTO `rbac_permissions` VALUES (391,'Command: gobject delete');
INSERT INTO `rbac_permissions` VALUES (392,'Command: gobject info');
INSERT INTO `rbac_permissions` VALUES (393,'Command: gobject move');
INSERT INTO `rbac_permissions` VALUES (394,'Command: gobject near');
INSERT INTO `rbac_permissions` VALUES (395,'Command: gobject set');
INSERT INTO `rbac_permissions` VALUES (396,'Command: gobject set phase');
INSERT INTO `rbac_permissions` VALUES (397,'Command: gobject set state');
INSERT INTO `rbac_permissions` VALUES (398,'Command: gobject target');
INSERT INTO `rbac_permissions` VALUES (399,'Command: gobject turn');
INSERT INTO `rbac_permissions` VALUES (400,'debug transport');
INSERT INTO `rbac_permissions` VALUES (401,'Command: guild');
INSERT INTO `rbac_permissions` VALUES (402,'Command: guild create');
INSERT INTO `rbac_permissions` VALUES (403,'Command: guild delete');
INSERT INTO `rbac_permissions` VALUES (404,'Command: guild invite');
INSERT INTO `rbac_permissions` VALUES (405,'Command: guild uninvite');
INSERT INTO `rbac_permissions` VALUES (406,'Command: guild rank');
INSERT INTO `rbac_permissions` VALUES (407,'Command: guild rename');
INSERT INTO `rbac_permissions` VALUES (408,'Command: honor');
INSERT INTO `rbac_permissions` VALUES (409,'Command: honor add');
INSERT INTO `rbac_permissions` VALUES (410,'Command: honor add kill');
INSERT INTO `rbac_permissions` VALUES (411,'Command: honor update');
INSERT INTO `rbac_permissions` VALUES (412,'Command: instance');
INSERT INTO `rbac_permissions` VALUES (413,'Command: instance listbinds');
INSERT INTO `rbac_permissions` VALUES (414,'Command: instance unbind');
INSERT INTO `rbac_permissions` VALUES (415,'Command: instance stats');
INSERT INTO `rbac_permissions` VALUES (417,'Command: learn');
INSERT INTO `rbac_permissions` VALUES (418,'Command: learn all');
INSERT INTO `rbac_permissions` VALUES (419,'Command: learn all my');
INSERT INTO `rbac_permissions` VALUES (420,'Command: learn all my class');
INSERT INTO `rbac_permissions` VALUES (421,'Command: learn all my pettalents');
INSERT INTO `rbac_permissions` VALUES (422,'Command: learn all my spells');
INSERT INTO `rbac_permissions` VALUES (423,'Command: learn all my talents');
INSERT INTO `rbac_permissions` VALUES (424,'Command: learn all gm');
INSERT INTO `rbac_permissions` VALUES (425,'Command: learn all crafts');
INSERT INTO `rbac_permissions` VALUES (426,'Command: learn all default');
INSERT INTO `rbac_permissions` VALUES (427,'Command: learn all lang');
INSERT INTO `rbac_permissions` VALUES (428,'Command: learn all recipes');
INSERT INTO `rbac_permissions` VALUES (429,'Command: unlearn');
INSERT INTO `rbac_permissions` VALUES (430,'Command: lfg');
INSERT INTO `rbac_permissions` VALUES (431,'Command: lfg player');
INSERT INTO `rbac_permissions` VALUES (432,'Command: lfg group');
INSERT INTO `rbac_permissions` VALUES (433,'Command: lfg queue');
INSERT INTO `rbac_permissions` VALUES (434,'Command: lfg clean');
INSERT INTO `rbac_permissions` VALUES (435,'Command: lfg options');
INSERT INTO `rbac_permissions` VALUES (436,'Command: list');
INSERT INTO `rbac_permissions` VALUES (437,'Command: list creature');
INSERT INTO `rbac_permissions` VALUES (438,'Command: list item');
INSERT INTO `rbac_permissions` VALUES (439,'Command: list object');
INSERT INTO `rbac_permissions` VALUES (440,'Command: list auras');
INSERT INTO `rbac_permissions` VALUES (441,'Command: list mail');
INSERT INTO `rbac_permissions` VALUES (442,'Command: lookup');
INSERT INTO `rbac_permissions` VALUES (443,'Command: lookup area');
INSERT INTO `rbac_permissions` VALUES (444,'Command: lookup creature');
INSERT INTO `rbac_permissions` VALUES (445,'Command: lookup event');
INSERT INTO `rbac_permissions` VALUES (446,'Command: lookup faction');
INSERT INTO `rbac_permissions` VALUES (447,'Command: lookup item');
INSERT INTO `rbac_permissions` VALUES (448,'Command: lookup itemset');
INSERT INTO `rbac_permissions` VALUES (449,'Command: lookup object');
INSERT INTO `rbac_permissions` VALUES (450,'Command: lookup quest');
INSERT INTO `rbac_permissions` VALUES (451,'Command: lookup player');
INSERT INTO `rbac_permissions` VALUES (452,'Command: lookup player ip');
INSERT INTO `rbac_permissions` VALUES (453,'Command: lookup player account');
INSERT INTO `rbac_permissions` VALUES (454,'Command: lookup player email');
INSERT INTO `rbac_permissions` VALUES (455,'Command: lookup skill');
INSERT INTO `rbac_permissions` VALUES (456,'Command: lookup spell');
INSERT INTO `rbac_permissions` VALUES (457,'Command: lookup spell id');
INSERT INTO `rbac_permissions` VALUES (458,'Command: lookup taxinode');
INSERT INTO `rbac_permissions` VALUES (459,'Command: lookup tele');
INSERT INTO `rbac_permissions` VALUES (460,'Command: lookup title');
INSERT INTO `rbac_permissions` VALUES (461,'Command: lookup map');
INSERT INTO `rbac_permissions` VALUES (462,'Command: announce');
INSERT INTO `rbac_permissions` VALUES (463,'Command: channel');
INSERT INTO `rbac_permissions` VALUES (464,'Command: channel set');
INSERT INTO `rbac_permissions` VALUES (465,'Command: channel set ownership');
INSERT INTO `rbac_permissions` VALUES (466,'Command: gmannounce');
INSERT INTO `rbac_permissions` VALUES (467,'Command: gmnameannounce');
INSERT INTO `rbac_permissions` VALUES (468,'Command: gmnotify');
INSERT INTO `rbac_permissions` VALUES (469,'Command: nameannounce');
INSERT INTO `rbac_permissions` VALUES (470,'Command: notify');
INSERT INTO `rbac_permissions` VALUES (471,'Command: whispers');
INSERT INTO `rbac_permissions` VALUES (472,'Command: group');
INSERT INTO `rbac_permissions` VALUES (473,'Command: group leader');
INSERT INTO `rbac_permissions` VALUES (474,'Command: group disband');
INSERT INTO `rbac_permissions` VALUES (475,'Command: group remove');
INSERT INTO `rbac_permissions` VALUES (476,'Command: group join');
INSERT INTO `rbac_permissions` VALUES (477,'Command: group list');
INSERT INTO `rbac_permissions` VALUES (478,'Command: group summon');
INSERT INTO `rbac_permissions` VALUES (479,'Command: pet');
INSERT INTO `rbac_permissions` VALUES (480,'Command: pet create');
INSERT INTO `rbac_permissions` VALUES (481,'Command: pet learn');
INSERT INTO `rbac_permissions` VALUES (482,'Command: pet unlearn');
INSERT INTO `rbac_permissions` VALUES (483,'Command: send');
INSERT INTO `rbac_permissions` VALUES (484,'Command: send items');
INSERT INTO `rbac_permissions` VALUES (485,'Command: send mail');
INSERT INTO `rbac_permissions` VALUES (486,'Command: send message');
INSERT INTO `rbac_permissions` VALUES (487,'Command: send money');
INSERT INTO `rbac_permissions` VALUES (488,'Command: additem');
INSERT INTO `rbac_permissions` VALUES (489,'Command: additemset');
INSERT INTO `rbac_permissions` VALUES (490,'Command: appear');
INSERT INTO `rbac_permissions` VALUES (491,'Command: aura');
INSERT INTO `rbac_permissions` VALUES (492,'Command: bank');
INSERT INTO `rbac_permissions` VALUES (493,'Command: bindsight');
INSERT INTO `rbac_permissions` VALUES (494,'Command: combatstop');
INSERT INTO `rbac_permissions` VALUES (495,'Command: cometome');
INSERT INTO `rbac_permissions` VALUES (496,'Command: commands');
INSERT INTO `rbac_permissions` VALUES (497,'Command: cooldown');
INSERT INTO `rbac_permissions` VALUES (498,'Command: damage');
INSERT INTO `rbac_permissions` VALUES (499,'Command: dev');
INSERT INTO `rbac_permissions` VALUES (500,'Command: die');
INSERT INTO `rbac_permissions` VALUES (501,'Command: dismount');
INSERT INTO `rbac_permissions` VALUES (502,'Command: distance');
INSERT INTO `rbac_permissions` VALUES (503,'Command: flusharenapoints');
INSERT INTO `rbac_permissions` VALUES (504,'Command: freeze');
INSERT INTO `rbac_permissions` VALUES (505,'Command: gps');
INSERT INTO `rbac_permissions` VALUES (506,'Command: guid');
INSERT INTO `rbac_permissions` VALUES (507,'Command: help');
INSERT INTO `rbac_permissions` VALUES (508,'Command: hidearea');
INSERT INTO `rbac_permissions` VALUES (509,'Command: itemmove');
INSERT INTO `rbac_permissions` VALUES (510,'Command: kick');
INSERT INTO `rbac_permissions` VALUES (511,'Command: linkgrave');
INSERT INTO `rbac_permissions` VALUES (512,'Command: listfreeze');
INSERT INTO `rbac_permissions` VALUES (513,'Command: maxskill');
INSERT INTO `rbac_permissions` VALUES (514,'Command: movegens');
INSERT INTO `rbac_permissions` VALUES (515,'Command: mute');
INSERT INTO `rbac_permissions` VALUES (516,'Command: neargrave');
INSERT INTO `rbac_permissions` VALUES (517,'Command: pinfo');
INSERT INTO `rbac_permissions` VALUES (518,'Command: playall');
INSERT INTO `rbac_permissions` VALUES (519,'Command: possess');
INSERT INTO `rbac_permissions` VALUES (520,'Command: recall');
INSERT INTO `rbac_permissions` VALUES (521,'Command: repairitems');
INSERT INTO `rbac_permissions` VALUES (522,'Command: respawn');
INSERT INTO `rbac_permissions` VALUES (523,'Command: revive');
INSERT INTO `rbac_permissions` VALUES (524,'Command: saveall');
INSERT INTO `rbac_permissions` VALUES (525,'Command: save');
INSERT INTO `rbac_permissions` VALUES (526,'Command: setskill');
INSERT INTO `rbac_permissions` VALUES (527,'Command: showarea');
INSERT INTO `rbac_permissions` VALUES (528,'Command: summon');
INSERT INTO `rbac_permissions` VALUES (529,'Command: unaura');
INSERT INTO `rbac_permissions` VALUES (530,'Command: unbindsight');
INSERT INTO `rbac_permissions` VALUES (531,'Command: unfreeze');
INSERT INTO `rbac_permissions` VALUES (532,'Command: unmute');
INSERT INTO `rbac_permissions` VALUES (533,'Command: unpossess');
INSERT INTO `rbac_permissions` VALUES (534,'Command: unstuck');
INSERT INTO `rbac_permissions` VALUES (535,'Command: wchange');
INSERT INTO `rbac_permissions` VALUES (536,'Command: mmap');
INSERT INTO `rbac_permissions` VALUES (537,'Command: mmap loadedtiles');
INSERT INTO `rbac_permissions` VALUES (538,'Command: mmap loc');
INSERT INTO `rbac_permissions` VALUES (539,'Command: mmap path');
INSERT INTO `rbac_permissions` VALUES (540,'Command: mmap stats');
INSERT INTO `rbac_permissions` VALUES (541,'Command: mmap testarea');
INSERT INTO `rbac_permissions` VALUES (542,'Command: morph');
INSERT INTO `rbac_permissions` VALUES (543,'Command: demorph');
INSERT INTO `rbac_permissions` VALUES (544,'Command: modify');
INSERT INTO `rbac_permissions` VALUES (545,'Command: modify arenapoints');
INSERT INTO `rbac_permissions` VALUES (547,'Command: modify drunk');
INSERT INTO `rbac_permissions` VALUES (548,'Command: modify energy');
INSERT INTO `rbac_permissions` VALUES (549,'Command: modify faction');
INSERT INTO `rbac_permissions` VALUES (550,'Command: modify gender');
INSERT INTO `rbac_permissions` VALUES (551,'Command: modify honor');
INSERT INTO `rbac_permissions` VALUES (552,'Command: modify hp');
INSERT INTO `rbac_permissions` VALUES (553,'Command: modify mana');
INSERT INTO `rbac_permissions` VALUES (554,'Command: modify money');
INSERT INTO `rbac_permissions` VALUES (555,'Command: modify mount');
INSERT INTO `rbac_permissions` VALUES (556,'Command: modify phase');
INSERT INTO `rbac_permissions` VALUES (557,'Command: modify rage');
INSERT INTO `rbac_permissions` VALUES (558,'Command: modify reputation');
INSERT INTO `rbac_permissions` VALUES (559,'Command: modify runicpower');
INSERT INTO `rbac_permissions` VALUES (560,'Command: modify scale');
INSERT INTO `rbac_permissions` VALUES (561,'Command: modify speed');
INSERT INTO `rbac_permissions` VALUES (562,'Command: modify speed all');
INSERT INTO `rbac_permissions` VALUES (563,'Command: modify speed backwalk');
INSERT INTO `rbac_permissions` VALUES (564,'Command: modify speed fly');
INSERT INTO `rbac_permissions` VALUES (565,'Command: modify speed walk');
INSERT INTO `rbac_permissions` VALUES (566,'Command: modify speed swim');
INSERT INTO `rbac_permissions` VALUES (567,'Command: modify spell');
INSERT INTO `rbac_permissions` VALUES (568,'Command: modify standstate');
INSERT INTO `rbac_permissions` VALUES (569,'Command: modify talentpoints');
INSERT INTO `rbac_permissions` VALUES (570,'Command: npc');
INSERT INTO `rbac_permissions` VALUES (571,'Command: npc add');
INSERT INTO `rbac_permissions` VALUES (572,'Command: npc add formation');
INSERT INTO `rbac_permissions` VALUES (573,'Command: npc add item');
INSERT INTO `rbac_permissions` VALUES (574,'Command: npc add move');
INSERT INTO `rbac_permissions` VALUES (575,'Command: npc add temp');
INSERT INTO `rbac_permissions` VALUES (576,'Command: npc add delete');
INSERT INTO `rbac_permissions` VALUES (577,'Command: npc add delete item');
INSERT INTO `rbac_permissions` VALUES (578,'Command: npc add follow');
INSERT INTO `rbac_permissions` VALUES (579,'Command: npc add follow stop');
INSERT INTO `rbac_permissions` VALUES (580,'Command: npc set');
INSERT INTO `rbac_permissions` VALUES (581,'Command: npc set allowmove');
INSERT INTO `rbac_permissions` VALUES (582,'Command: npc set entry');
INSERT INTO `rbac_permissions` VALUES (583,'Command: npc set factionid');
INSERT INTO `rbac_permissions` VALUES (584,'Command: npc set flag');
INSERT INTO `rbac_permissions` VALUES (585,'Command: npc set level');
INSERT INTO `rbac_permissions` VALUES (586,'Command: npc set link');
INSERT INTO `rbac_permissions` VALUES (587,'Command: npc set model');
INSERT INTO `rbac_permissions` VALUES (588,'Command: npc set movetype');
INSERT INTO `rbac_permissions` VALUES (589,'Command: npc set phase');
INSERT INTO `rbac_permissions` VALUES (590,'Command: npc set spawndist');
INSERT INTO `rbac_permissions` VALUES (591,'Command: npc set spawntime');
INSERT INTO `rbac_permissions` VALUES (592,'Command: npc set data');
INSERT INTO `rbac_permissions` VALUES (593,'Command: npc info');
INSERT INTO `rbac_permissions` VALUES (594,'Command: npc near');
INSERT INTO `rbac_permissions` VALUES (595,'Command: npc move');
INSERT INTO `rbac_permissions` VALUES (596,'Command: npc playemote');
INSERT INTO `rbac_permissions` VALUES (597,'Command: npc say');
INSERT INTO `rbac_permissions` VALUES (598,'Command: npc textemote');
INSERT INTO `rbac_permissions` VALUES (599,'Command: npc whisper');
INSERT INTO `rbac_permissions` VALUES (600,'Command: npc yell');
INSERT INTO `rbac_permissions` VALUES (601,'Command: npc tame');
INSERT INTO `rbac_permissions` VALUES (602,'Command: quest');
INSERT INTO `rbac_permissions` VALUES (603,'Command: quest add');
INSERT INTO `rbac_permissions` VALUES (604,'Command: quest complete');
INSERT INTO `rbac_permissions` VALUES (605,'Command: quest remove');
INSERT INTO `rbac_permissions` VALUES (606,'Command: quest reward');
INSERT INTO `rbac_permissions` VALUES (607,'Command: reload');
INSERT INTO `rbac_permissions` VALUES (608,'Command: reload access_requirement');
INSERT INTO `rbac_permissions` VALUES (609,'Command: reload criteria_data');
INSERT INTO `rbac_permissions` VALUES (610,'Command: reload achievement_reward');
INSERT INTO `rbac_permissions` VALUES (611,'Command: reload all');
INSERT INTO `rbac_permissions` VALUES (612,'Command: reload all achievement');
INSERT INTO `rbac_permissions` VALUES (613,'Command: reload all area');
INSERT INTO `rbac_permissions` VALUES (615,'Command: reload all gossips');
INSERT INTO `rbac_permissions` VALUES (616,'Command: reload all item');
INSERT INTO `rbac_permissions` VALUES (617,'Command: reload all locales');
INSERT INTO `rbac_permissions` VALUES (618,'Command: reload all loot');
INSERT INTO `rbac_permissions` VALUES (619,'Command: reload all npc');
INSERT INTO `rbac_permissions` VALUES (620,'Command: reload all quest');
INSERT INTO `rbac_permissions` VALUES (621,'Command: reload all scripts');
INSERT INTO `rbac_permissions` VALUES (623,'Command: reload areatrigger_involvedrelation');
INSERT INTO `rbac_permissions` VALUES (624,'Command: reload areatrigger_tavern');
INSERT INTO `rbac_permissions` VALUES (625,'Command: reload areatrigger_teleport');
INSERT INTO `rbac_permissions` VALUES (626,'Command: reload auctions');
INSERT INTO `rbac_permissions` VALUES (627,'Command: reload autobroadcast');
INSERT INTO `rbac_permissions` VALUES (628,'Command: reload command');
INSERT INTO `rbac_permissions` VALUES (629,'Command: reload conditions');
INSERT INTO `rbac_permissions` VALUES (630,'Command: reload config');
INSERT INTO `rbac_permissions` VALUES (631,'Command: reload battleground_template');
INSERT INTO `rbac_permissions` VALUES (632,'Command: .mutehistory');
INSERT INTO `rbac_permissions` VALUES (633,'Command: reload creature_linked_respawn');
INSERT INTO `rbac_permissions` VALUES (634,'Command: reload creature_loot_template');
INSERT INTO `rbac_permissions` VALUES (635,'Command: reload creature_onkill_reputation');
INSERT INTO `rbac_permissions` VALUES (636,'Command: reload creature_questender');
INSERT INTO `rbac_permissions` VALUES (637,'Command: reload creature_queststarter');
INSERT INTO `rbac_permissions` VALUES (638,'Command: reload creature_summon_groups');
INSERT INTO `rbac_permissions` VALUES (639,'Command: reload creature_template');
INSERT INTO `rbac_permissions` VALUES (640,'Command: reload creature_text');
INSERT INTO `rbac_permissions` VALUES (641,'Command: reload disables');
INSERT INTO `rbac_permissions` VALUES (642,'Command: reload disenchant_loot_template');
INSERT INTO `rbac_permissions` VALUES (643,'Command: reload event_scripts');
INSERT INTO `rbac_permissions` VALUES (644,'Command: reload fishing_loot_template');
INSERT INTO `rbac_permissions` VALUES (645,'Command: reload graveyard_zone');
INSERT INTO `rbac_permissions` VALUES (646,'Command: reload game_tele');
INSERT INTO `rbac_permissions` VALUES (647,'Command: reload gameobject_questender');
INSERT INTO `rbac_permissions` VALUES (648,'Command: reload gameobject_loot_template');
INSERT INTO `rbac_permissions` VALUES (649,'Command: reload gameobject_queststarter');
INSERT INTO `rbac_permissions` VALUES (650,'Command: reload support');
INSERT INTO `rbac_permissions` VALUES (651,'Command: reload gossip_menu');
INSERT INTO `rbac_permissions` VALUES (652,'Command: reload gossip_menu_option');
INSERT INTO `rbac_permissions` VALUES (653,'Command: reload item_random_bonus_list_template');
INSERT INTO `rbac_permissions` VALUES (654,'Command: reload item_loot_template');
INSERT INTO `rbac_permissions` VALUES (655,'Command: reload item_set_names');
INSERT INTO `rbac_permissions` VALUES (656,'Command: reload lfg_dungeon_rewards');
INSERT INTO `rbac_permissions` VALUES (657,'Command: reload locales_achievement_reward');
INSERT INTO `rbac_permissions` VALUES (658,'Command: reload locales_creature');
INSERT INTO `rbac_permissions` VALUES (659,'Command: reload locales_creature_text');
INSERT INTO `rbac_permissions` VALUES (660,'Command: reload locales_gameobject');
INSERT INTO `rbac_permissions` VALUES (661,'Command: reload locales_gossip_menu_option');
INSERT INTO `rbac_permissions` VALUES (663,'Command: reload locales_item_set_name');
INSERT INTO `rbac_permissions` VALUES (665,'Command: reload locales_page_text');
INSERT INTO `rbac_permissions` VALUES (666,'Command: reload locales_points_of_interest');
INSERT INTO `rbac_permissions` VALUES (667,'Command: reload quest_locale');
INSERT INTO `rbac_permissions` VALUES (668,'Command: reload mail_level_reward');
INSERT INTO `rbac_permissions` VALUES (669,'Command: reload mail_loot_template');
INSERT INTO `rbac_permissions` VALUES (670,'Command: reload milling_loot_template');
INSERT INTO `rbac_permissions` VALUES (671,'Command: reload npc_spellclick_spells');
INSERT INTO `rbac_permissions` VALUES (672,'Command: reload npc_trainer');
INSERT INTO `rbac_permissions` VALUES (673,'Command: reload npc_vendor');
INSERT INTO `rbac_permissions` VALUES (674,'Command: reload page_text');
INSERT INTO `rbac_permissions` VALUES (675,'Command: reload pickpocketing_loot_template');
INSERT INTO `rbac_permissions` VALUES (676,'Command: reload points_of_interest');
INSERT INTO `rbac_permissions` VALUES (677,'Command: reload prospecting_loot_template');
INSERT INTO `rbac_permissions` VALUES (678,'Command: reload quest_poi');
INSERT INTO `rbac_permissions` VALUES (679,'Command: reload quest_template');
INSERT INTO `rbac_permissions` VALUES (680,'Command: reload rbac');
INSERT INTO `rbac_permissions` VALUES (681,'Command: reload reference_loot_template');
INSERT INTO `rbac_permissions` VALUES (682,'Command: reload reserved_name');
INSERT INTO `rbac_permissions` VALUES (683,'Command: reload reputation_reward_rate');
INSERT INTO `rbac_permissions` VALUES (684,'Command: reload reputation_spillover_template');
INSERT INTO `rbac_permissions` VALUES (685,'Command: reload skill_discovery_template');
INSERT INTO `rbac_permissions` VALUES (686,'Command: reload skill_extra_item_template');
INSERT INTO `rbac_permissions` VALUES (687,'Command: reload skill_fishing_base_level');
INSERT INTO `rbac_permissions` VALUES (688,'Command: reload skinning_loot_template');
INSERT INTO `rbac_permissions` VALUES (689,'Command: reload smart_scripts');
INSERT INTO `rbac_permissions` VALUES (690,'Command: reload spell_required');
INSERT INTO `rbac_permissions` VALUES (691,'Command: reload spell_area');
INSERT INTO `rbac_permissions` VALUES (693,'Command: reload spell_group');
INSERT INTO `rbac_permissions` VALUES (694,'Command: reload spell_learn_spell');
INSERT INTO `rbac_permissions` VALUES (695,'Command: reload spell_loot_template');
INSERT INTO `rbac_permissions` VALUES (696,'Command: reload spell_linked_spell');
INSERT INTO `rbac_permissions` VALUES (697,'Command: reload spell_pet_auras');
INSERT INTO `rbac_permissions` VALUES (698,'Command: character changeaccount');
INSERT INTO `rbac_permissions` VALUES (699,'Command: reload spell_proc');
INSERT INTO `rbac_permissions` VALUES (701,'Command: reload spell_target_position');
INSERT INTO `rbac_permissions` VALUES (702,'Command: reload spell_threats');
INSERT INTO `rbac_permissions` VALUES (703,'Command: reload spell_group_stack_rules');
INSERT INTO `rbac_permissions` VALUES (704,'Command: reload trinity_string');
INSERT INTO `rbac_permissions` VALUES (707,'Command: reload waypoint_path');
INSERT INTO `rbac_permissions` VALUES (708,'Command: reload vehicle_accessory');
INSERT INTO `rbac_permissions` VALUES (709,'Command: reload vehicle_template_accessory');
INSERT INTO `rbac_permissions` VALUES (710,'Command: reset');
INSERT INTO `rbac_permissions` VALUES (711,'Command: reset achievements');
INSERT INTO `rbac_permissions` VALUES (712,'Command: reset honor');
INSERT INTO `rbac_permissions` VALUES (713,'Command: reset level');
INSERT INTO `rbac_permissions` VALUES (714,'Command: reset spells');
INSERT INTO `rbac_permissions` VALUES (715,'Command: reset stats');
INSERT INTO `rbac_permissions` VALUES (716,'Command: reset talents');
INSERT INTO `rbac_permissions` VALUES (717,'Command: reset all');
INSERT INTO `rbac_permissions` VALUES (718,'Command: server');
INSERT INTO `rbac_permissions` VALUES (719,'Command: server corpses');
INSERT INTO `rbac_permissions` VALUES (720,'Command: server exit');
INSERT INTO `rbac_permissions` VALUES (721,'Command: server idlerestart');
INSERT INTO `rbac_permissions` VALUES (722,'Command: server idlerestart cancel');
INSERT INTO `rbac_permissions` VALUES (723,'Command: server idleshutdown');
INSERT INTO `rbac_permissions` VALUES (724,'Command: server idleshutdown cancel');
INSERT INTO `rbac_permissions` VALUES (725,'Command: server info');
INSERT INTO `rbac_permissions` VALUES (726,'Command: server plimit');
INSERT INTO `rbac_permissions` VALUES (727,'Command: server restart');
INSERT INTO `rbac_permissions` VALUES (728,'Command: server restart cancel');
INSERT INTO `rbac_permissions` VALUES (729,'Command: server set');
INSERT INTO `rbac_permissions` VALUES (730,'Command: server set closed');
INSERT INTO `rbac_permissions` VALUES (731,'Command: server set difftime');
INSERT INTO `rbac_permissions` VALUES (732,'Command: server set loglevel');
INSERT INTO `rbac_permissions` VALUES (733,'Command: server set motd');
INSERT INTO `rbac_permissions` VALUES (734,'Command: server shutdown');
INSERT INTO `rbac_permissions` VALUES (735,'Command: server shutdown cancel');
INSERT INTO `rbac_permissions` VALUES (736,'Command: server motd');
INSERT INTO `rbac_permissions` VALUES (737,'Command: tele');
INSERT INTO `rbac_permissions` VALUES (738,'Command: tele add');
INSERT INTO `rbac_permissions` VALUES (739,'Command: tele del');
INSERT INTO `rbac_permissions` VALUES (740,'Command: tele name');
INSERT INTO `rbac_permissions` VALUES (741,'Command: tele group');
INSERT INTO `rbac_permissions` VALUES (742,'Command: ticket');
INSERT INTO `rbac_permissions` VALUES (753,'Command: ticket reset');
INSERT INTO `rbac_permissions` VALUES (757,'Command: ticket togglesystem');
INSERT INTO `rbac_permissions` VALUES (761,'Command: titles');
INSERT INTO `rbac_permissions` VALUES (762,'Command: titles add');
INSERT INTO `rbac_permissions` VALUES (763,'Command: titles current');
INSERT INTO `rbac_permissions` VALUES (764,'Command: titles remove');
INSERT INTO `rbac_permissions` VALUES (765,'Command: titles set');
INSERT INTO `rbac_permissions` VALUES (766,'Command: titles set mask');
INSERT INTO `rbac_permissions` VALUES (767,'Command: wp');
INSERT INTO `rbac_permissions` VALUES (768,'Command: wp add');
INSERT INTO `rbac_permissions` VALUES (769,'Command: wp event');
INSERT INTO `rbac_permissions` VALUES (770,'Command: wp load');
INSERT INTO `rbac_permissions` VALUES (771,'Command: wp modify');
INSERT INTO `rbac_permissions` VALUES (772,'Command: wp unload');
INSERT INTO `rbac_permissions` VALUES (773,'Command: wp reload');
INSERT INTO `rbac_permissions` VALUES (774,'Command: wp show');
INSERT INTO `rbac_permissions` VALUES (775,'Command: modify currency');
INSERT INTO `rbac_permissions` VALUES (776,'Command: debug phase');
INSERT INTO `rbac_permissions` VALUES (777,'Command: mailbox');
INSERT INTO `rbac_permissions` VALUES (778,'Command: ahbot');
INSERT INTO `rbac_permissions` VALUES (779,'Command: ahbot items');
INSERT INTO `rbac_permissions` VALUES (780,'Command: ahbot items gray');
INSERT INTO `rbac_permissions` VALUES (781,'Command: ahbot items white');
INSERT INTO `rbac_permissions` VALUES (782,'Command: ahbot items green');
INSERT INTO `rbac_permissions` VALUES (783,'Command: ahbot items blue');
INSERT INTO `rbac_permissions` VALUES (784,'Command: ahbot items purple');
INSERT INTO `rbac_permissions` VALUES (785,'Command: ahbot items orange');
INSERT INTO `rbac_permissions` VALUES (786,'Command: ahbot items yellow');
INSERT INTO `rbac_permissions` VALUES (787,'Command: ahbot ratio');
INSERT INTO `rbac_permissions` VALUES (788,'Command: ahbot ratio alliance');
INSERT INTO `rbac_permissions` VALUES (789,'Command: ahbot ratio horde');
INSERT INTO `rbac_permissions` VALUES (790,'Command: ahbot ratio neutral');
INSERT INTO `rbac_permissions` VALUES (791,'Command: ahbot rebuild');
INSERT INTO `rbac_permissions` VALUES (792,'Command: ahbot reload');
INSERT INTO `rbac_permissions` VALUES (793,'Command: ahbot status');
INSERT INTO `rbac_permissions` VALUES (794,'Command: .guild info');
INSERT INTO `rbac_permissions` VALUES (795,'Command: instance setbossstate');
INSERT INTO `rbac_permissions` VALUES (796,'Command: instance getbossstate');
INSERT INTO `rbac_permissions` VALUES (797,'Command: pvpstats');
INSERT INTO `rbac_permissions` VALUES (798,'Command: .mod xp');
INSERT INTO `rbac_permissions` VALUES (802,'Command: .ticket bug');
INSERT INTO `rbac_permissions` VALUES (803,'Command: .ticket complaint');
INSERT INTO `rbac_permissions` VALUES (804,'Command: .ticket suggestion');
INSERT INTO `rbac_permissions` VALUES (805,'Command: .ticket bug assign');
INSERT INTO `rbac_permissions` VALUES (806,'Command: .ticket bug close');
INSERT INTO `rbac_permissions` VALUES (807,'Command: .ticket bug closedlist');
INSERT INTO `rbac_permissions` VALUES (808,'Command: .ticket bug comment');
INSERT INTO `rbac_permissions` VALUES (809,'Command: .ticket bug delete');
INSERT INTO `rbac_permissions` VALUES (810,'Command: .ticket bug list');
INSERT INTO `rbac_permissions` VALUES (811,'Command: .ticket bug unassign');
INSERT INTO `rbac_permissions` VALUES (812,'Command: .ticket bug view');
INSERT INTO `rbac_permissions` VALUES (813,'Command: .ticket complaint assign');
INSERT INTO `rbac_permissions` VALUES (814,'Command: .ticket complaint close');
INSERT INTO `rbac_permissions` VALUES (815,'Command: .ticket complaint closedlist');
INSERT INTO `rbac_permissions` VALUES (816,'Command: .ticket complaint comment');
INSERT INTO `rbac_permissions` VALUES (817,'Command: .ticket complaint delete');
INSERT INTO `rbac_permissions` VALUES (818,'Command: .ticket complaint list');
INSERT INTO `rbac_permissions` VALUES (819,'Command: .ticket complaint unassign');
INSERT INTO `rbac_permissions` VALUES (820,'Command: .ticket complaint view');
INSERT INTO `rbac_permissions` VALUES (821,'Command: .ticket suggestion assign');
INSERT INTO `rbac_permissions` VALUES (822,'Command: .ticket suggestion close');
INSERT INTO `rbac_permissions` VALUES (823,'Command: .ticket suggestion closedlist');
INSERT INTO `rbac_permissions` VALUES (824,'Command: .ticket suggestion comment');
INSERT INTO `rbac_permissions` VALUES (825,'Command: .ticket suggestion delete');
INSERT INTO `rbac_permissions` VALUES (826,'Command: .ticket suggestion list');
INSERT INTO `rbac_permissions` VALUES (827,'Command: .ticket suggestion unassign');
INSERT INTO `rbac_permissions` VALUES (828,'Command: .ticket suggestion view');
INSERT INTO `rbac_permissions` VALUES (829,'Command: .ticket reset all');
INSERT INTO `rbac_permissions` VALUES (830,'Command: .bnetaccount listgameaccounts');
INSERT INTO `rbac_permissions` VALUES (831,'Command: .ticket reset bug');
INSERT INTO `rbac_permissions` VALUES (832,'Command: .ticket reset complaint');
INSERT INTO `rbac_permissions` VALUES (833,'Command: .ticket reset suggestion');
INSERT INTO `rbac_permissions` VALUES (835,'Command: debug loadcells');
INSERT INTO `rbac_permissions` VALUES (836,'Command: .debug boundary');
INSERT INTO `rbac_permissions` VALUES (837,'Command: npc evade');
INSERT INTO `rbac_permissions` VALUES (838,'Command: pet level');
INSERT INTO `rbac_permissions` VALUES (839,'Command: server shutdown force');
INSERT INTO `rbac_permissions` VALUES (840,'Command: server restart force');
INSERT INTO `rbac_permissions` VALUES (842,'Command: reload character_template');
INSERT INTO `rbac_permissions` VALUES (843,'Command: reload quest_greeting');
INSERT INTO `rbac_permissions` VALUES (844,'Command: scene');
INSERT INTO `rbac_permissions` VALUES (845,'Command: scene debug');
INSERT INTO `rbac_permissions` VALUES (846,'Command: scene play');
INSERT INTO `rbac_permissions` VALUES (847,'Command: scene play package');
INSERT INTO `rbac_permissions` VALUES (848,'Command: scene cancel');
INSERT INTO `rbac_permissions` VALUES (849,'Command: list scenes');
INSERT INTO `rbac_permissions` VALUES (850,'Command: reload scenes');
INSERT INTO `rbac_permissions` VALUES (851,'Command: reload areatrigger_templates');
INSERT INTO `rbac_permissions` VALUES (852,'Command: debug dummy');
INSERT INTO `rbac_permissions` VALUES (853,'Command: .reload conversation_template');
INSERT INTO `rbac_permissions` VALUES (854,'Command: .debug conversation');
INSERT INTO `rbac_permissions` VALUES (855,'Command: debug play music');
INSERT INTO `rbac_permissions` VALUES (856,'Command: npc spawngroup');
INSERT INTO `rbac_permissions` VALUES (857,'Command: npc despawngroup');
INSERT INTO `rbac_permissions` VALUES (858,'Command: gobject spawngroup');
INSERT INTO `rbac_permissions` VALUES (859,'Command: gobject despawngroup');
INSERT INTO `rbac_permissions` VALUES (860,'Command: list respawns');
INSERT INTO `rbac_permissions` VALUES (865,'Command: npc showloot');
INSERT INTO `rbac_permissions` VALUES (866,'Command: list spawnpoints');
INSERT INTO `rbac_permissions` VALUES (868,'Command: modify power');
INSERT INTO `rbac_permissions` VALUES (869,'Command: debug send playerchoice');
INSERT INTO `rbac_permissions` VALUES (870,'Command: debug threatinfo');
INSERT INTO `rbac_permissions` VALUES (871,'Command: debug instancespawn');
INSERT INTO `rbac_permissions` VALUES (872,'Command: server debug');
INSERT INTO `rbac_permissions` VALUES (873,'Command: reload creature_movement_override');
INSERT INTO `rbac_permissions` VALUES (874,'Command: debug asan');
INSERT INTO `rbac_permissions` VALUES (875,'Command: lookup map id');
INSERT INTO `rbac_permissions` VALUES (876,'Command: lookup item id');
INSERT INTO `rbac_permissions` VALUES (877,'Command: lookup quest id');
INSERT INTO `rbac_permissions` VALUES (878,'Command: debug questreset');
INSERT INTO `rbac_permissions` VALUES (879,'Command: debug poolstatus');
INSERT INTO `rbac_permissions` VALUES (880,'Command: pdump copy');
INSERT INTO `rbac_permissions` VALUES (881,'Command: reload vehicle_template');
INSERT INTO `rbac_permissions` VALUES (882,'Command: reload spell_script_names');
INSERT INTO `rbac_permissions` VALUES (883,'Command: quest objective complete');
INSERT INTO `rbac_permissions` VALUES (884,'Command: bg start');
INSERT INTO `rbac_permissions` VALUES (885,'Command: bg stop');
/*!40000 ALTER TABLE `rbac_permissions` ENABLE KEYS */;

/*!40000 ALTER TABLE `rbac_linked_permissions` DISABLE KEYS */;
INSERT INTO `rbac_linked_permissions` VALUES (192,7);
INSERT INTO `rbac_linked_permissions` VALUES (192,21);
INSERT INTO `rbac_linked_permissions` VALUES (192,42);
INSERT INTO `rbac_linked_permissions` VALUES (192,43);
INSERT INTO `rbac_linked_permissions` VALUES (192,193);
INSERT INTO `rbac_linked_permissions` VALUES (192,196);
INSERT INTO `rbac_linked_permissions` VALUES (192,776);
INSERT INTO `rbac_linked_permissions` VALUES (192,778);
INSERT INTO `rbac_linked_permissions` VALUES (192,779);
INSERT INTO `rbac_linked_permissions` VALUES (192,780);
INSERT INTO `rbac_linked_permissions` VALUES (192,781);
INSERT INTO `rbac_linked_permissions` VALUES (192,782);
INSERT INTO `rbac_linked_permissions` VALUES (192,783);
INSERT INTO `rbac_linked_permissions` VALUES (192,784);
INSERT INTO `rbac_linked_permissions` VALUES (192,785);
INSERT INTO `rbac_linked_permissions` VALUES (192,786);
INSERT INTO `rbac_linked_permissions` VALUES (192,787);
INSERT INTO `rbac_linked_permissions` VALUES (192,788);
INSERT INTO `rbac_linked_permissions` VALUES (192,789);
INSERT INTO `rbac_linked_permissions` VALUES (192,790);
INSERT INTO `rbac_linked_permissions` VALUES (192,791);
INSERT INTO `rbac_linked_permissions` VALUES (192,792);
INSERT INTO `rbac_linked_permissions` VALUES (192,793);
INSERT INTO `rbac_linked_permissions` VALUES (192,794);
INSERT INTO `rbac_linked_permissions` VALUES (192,795);
INSERT INTO `rbac_linked_permissions` VALUES (192,796);
INSERT INTO `rbac_linked_permissions` VALUES (192,835);
INSERT INTO `rbac_linked_permissions` VALUES (192,844);
INSERT INTO `rbac_linked_permissions` VALUES (192,845);
INSERT INTO `rbac_linked_permissions` VALUES (192,846);
INSERT INTO `rbac_linked_permissions` VALUES (192,847);
INSERT INTO `rbac_linked_permissions` VALUES (192,848);
INSERT INTO `rbac_linked_permissions` VALUES (192,849);
INSERT INTO `rbac_linked_permissions` VALUES (192,850);
INSERT INTO `rbac_linked_permissions` VALUES (192,851);
INSERT INTO `rbac_linked_permissions` VALUES (192,853);
INSERT INTO `rbac_linked_permissions` VALUES (192,854);
INSERT INTO `rbac_linked_permissions` VALUES (193,48);
INSERT INTO `rbac_linked_permissions` VALUES (193,52);
INSERT INTO `rbac_linked_permissions` VALUES (193,53);
INSERT INTO `rbac_linked_permissions` VALUES (193,194);
INSERT INTO `rbac_linked_permissions` VALUES (193,197);
INSERT INTO `rbac_linked_permissions` VALUES (194,1);
INSERT INTO `rbac_linked_permissions` VALUES (194,2);
INSERT INTO `rbac_linked_permissions` VALUES (194,9);
INSERT INTO `rbac_linked_permissions` VALUES (194,11);
INSERT INTO `rbac_linked_permissions` VALUES (194,12);
INSERT INTO `rbac_linked_permissions` VALUES (194,13);
INSERT INTO `rbac_linked_permissions` VALUES (194,14);
INSERT INTO `rbac_linked_permissions` VALUES (194,15);
INSERT INTO `rbac_linked_permissions` VALUES (194,16);
INSERT INTO `rbac_linked_permissions` VALUES (194,17);
INSERT INTO `rbac_linked_permissions` VALUES (194,18);
INSERT INTO `rbac_linked_permissions` VALUES (194,19);
INSERT INTO `rbac_linked_permissions` VALUES (194,20);
INSERT INTO `rbac_linked_permissions` VALUES (194,22);
INSERT INTO `rbac_linked_permissions` VALUES (194,23);
INSERT INTO `rbac_linked_permissions` VALUES (194,25);
INSERT INTO `rbac_linked_permissions` VALUES (194,26);
INSERT INTO `rbac_linked_permissions` VALUES (194,27);
INSERT INTO `rbac_linked_permissions` VALUES (194,28);
INSERT INTO `rbac_linked_permissions` VALUES (194,29);
INSERT INTO `rbac_linked_permissions` VALUES (194,30);
INSERT INTO `rbac_linked_permissions` VALUES (194,31);
INSERT INTO `rbac_linked_permissions` VALUES (194,32);
INSERT INTO `rbac_linked_permissions` VALUES (194,33);
INSERT INTO `rbac_linked_permissions` VALUES (194,34);
INSERT INTO `rbac_linked_permissions` VALUES (194,35);
INSERT INTO `rbac_linked_permissions` VALUES (194,36);
INSERT INTO `rbac_linked_permissions` VALUES (194,37);
INSERT INTO `rbac_linked_permissions` VALUES (194,38);
INSERT INTO `rbac_linked_permissions` VALUES (194,39);
INSERT INTO `rbac_linked_permissions` VALUES (194,40);
INSERT INTO `rbac_linked_permissions` VALUES (194,41);
INSERT INTO `rbac_linked_permissions` VALUES (194,44);
INSERT INTO `rbac_linked_permissions` VALUES (194,46);
INSERT INTO `rbac_linked_permissions` VALUES (194,47);
INSERT INTO `rbac_linked_permissions` VALUES (194,51);
INSERT INTO `rbac_linked_permissions` VALUES (194,195);
INSERT INTO `rbac_linked_permissions` VALUES (194,198);
INSERT INTO `rbac_linked_permissions` VALUES (194,632);
INSERT INTO `rbac_linked_permissions` VALUES (194,798);
INSERT INTO `rbac_linked_permissions` VALUES (195,3);
INSERT INTO `rbac_linked_permissions` VALUES (195,4);
INSERT INTO `rbac_linked_permissions` VALUES (195,5);
INSERT INTO `rbac_linked_permissions` VALUES (195,6);
INSERT INTO `rbac_linked_permissions` VALUES (195,24);
INSERT INTO `rbac_linked_permissions` VALUES (195,49);
INSERT INTO `rbac_linked_permissions` VALUES (195,199);
INSERT INTO `rbac_linked_permissions` VALUES (196,10);
INSERT INTO `rbac_linked_permissions` VALUES (196,202);
INSERT INTO `rbac_linked_permissions` VALUES (196,203);
INSERT INTO `rbac_linked_permissions` VALUES (196,204);
INSERT INTO `rbac_linked_permissions` VALUES (196,205);
INSERT INTO `rbac_linked_permissions` VALUES (196,206);
INSERT INTO `rbac_linked_permissions` VALUES (196,208);
INSERT INTO `rbac_linked_permissions` VALUES (196,212);
INSERT INTO `rbac_linked_permissions` VALUES (196,213);
INSERT INTO `rbac_linked_permissions` VALUES (196,214);
INSERT INTO `rbac_linked_permissions` VALUES (196,215);
INSERT INTO `rbac_linked_permissions` VALUES (196,216);
INSERT INTO `rbac_linked_permissions` VALUES (196,226);
INSERT INTO `rbac_linked_permissions` VALUES (196,227);
INSERT INTO `rbac_linked_permissions` VALUES (196,230);
INSERT INTO `rbac_linked_permissions` VALUES (196,231);
INSERT INTO `rbac_linked_permissions` VALUES (196,233);
INSERT INTO `rbac_linked_permissions` VALUES (196,234);
INSERT INTO `rbac_linked_permissions` VALUES (196,235);
INSERT INTO `rbac_linked_permissions` VALUES (196,238);
INSERT INTO `rbac_linked_permissions` VALUES (196,239);
INSERT INTO `rbac_linked_permissions` VALUES (196,240);
INSERT INTO `rbac_linked_permissions` VALUES (196,241);
INSERT INTO `rbac_linked_permissions` VALUES (196,242);
INSERT INTO `rbac_linked_permissions` VALUES (196,243);
INSERT INTO `rbac_linked_permissions` VALUES (196,244);
INSERT INTO `rbac_linked_permissions` VALUES (196,245);
INSERT INTO `rbac_linked_permissions` VALUES (196,246);
INSERT INTO `rbac_linked_permissions` VALUES (196,247);
INSERT INTO `rbac_linked_permissions` VALUES (196,248);
INSERT INTO `rbac_linked_permissions` VALUES (196,249);
INSERT INTO `rbac_linked_permissions` VALUES (196,250);
INSERT INTO `rbac_linked_permissions` VALUES (196,251);
INSERT INTO `rbac_linked_permissions` VALUES (196,252);
INSERT INTO `rbac_linked_permissions` VALUES (196,253);
INSERT INTO `rbac_linked_permissions` VALUES (196,254);
INSERT INTO `rbac_linked_permissions` VALUES (196,255);
INSERT INTO `rbac_linked_permissions` VALUES (196,256);
INSERT INTO `rbac_linked_permissions` VALUES (196,257);
INSERT INTO `rbac_linked_permissions` VALUES (196,258);
INSERT INTO `rbac_linked_permissions` VALUES (196,259);
INSERT INTO `rbac_linked_permissions` VALUES (196,260);
INSERT INTO `rbac_linked_permissions` VALUES (196,261);
INSERT INTO `rbac_linked_permissions` VALUES (196,262);
INSERT INTO `rbac_linked_permissions` VALUES (196,264);
INSERT INTO `rbac_linked_permissions` VALUES (196,265);
INSERT INTO `rbac_linked_permissions` VALUES (196,266);
INSERT INTO `rbac_linked_permissions` VALUES (196,267);
INSERT INTO `rbac_linked_permissions` VALUES (196,268);
INSERT INTO `rbac_linked_permissions` VALUES (196,269);
INSERT INTO `rbac_linked_permissions` VALUES (196,270);
INSERT INTO `rbac_linked_permissions` VALUES (196,271);
INSERT INTO `rbac_linked_permissions` VALUES (196,272);
INSERT INTO `rbac_linked_permissions` VALUES (196,279);
INSERT INTO `rbac_linked_permissions` VALUES (196,280);
INSERT INTO `rbac_linked_permissions` VALUES (196,283);
INSERT INTO `rbac_linked_permissions` VALUES (196,287);
INSERT INTO `rbac_linked_permissions` VALUES (196,288);
INSERT INTO `rbac_linked_permissions` VALUES (196,289);
INSERT INTO `rbac_linked_permissions` VALUES (196,290);
INSERT INTO `rbac_linked_permissions` VALUES (196,291);
INSERT INTO `rbac_linked_permissions` VALUES (196,292);
INSERT INTO `rbac_linked_permissions` VALUES (196,293);
INSERT INTO `rbac_linked_permissions` VALUES (196,294);
INSERT INTO `rbac_linked_permissions` VALUES (196,295);
INSERT INTO `rbac_linked_permissions` VALUES (196,296);
INSERT INTO `rbac_linked_permissions` VALUES (196,297);
INSERT INTO `rbac_linked_permissions` VALUES (196,298);
INSERT INTO `rbac_linked_permissions` VALUES (196,299);
INSERT INTO `rbac_linked_permissions` VALUES (196,302);
INSERT INTO `rbac_linked_permissions` VALUES (196,303);
INSERT INTO `rbac_linked_permissions` VALUES (196,304);
INSERT INTO `rbac_linked_permissions` VALUES (196,305);
INSERT INTO `rbac_linked_permissions` VALUES (196,306);
INSERT INTO `rbac_linked_permissions` VALUES (196,309);
INSERT INTO `rbac_linked_permissions` VALUES (196,310);
INSERT INTO `rbac_linked_permissions` VALUES (196,314);
INSERT INTO `rbac_linked_permissions` VALUES (196,319);
INSERT INTO `rbac_linked_permissions` VALUES (196,320);
INSERT INTO `rbac_linked_permissions` VALUES (196,321);
INSERT INTO `rbac_linked_permissions` VALUES (196,322);
INSERT INTO `rbac_linked_permissions` VALUES (196,323);
INSERT INTO `rbac_linked_permissions` VALUES (196,324);
INSERT INTO `rbac_linked_permissions` VALUES (196,325);
INSERT INTO `rbac_linked_permissions` VALUES (196,326);
INSERT INTO `rbac_linked_permissions` VALUES (196,327);
INSERT INTO `rbac_linked_permissions` VALUES (196,328);
INSERT INTO `rbac_linked_permissions` VALUES (196,329);
INSERT INTO `rbac_linked_permissions` VALUES (196,330);
INSERT INTO `rbac_linked_permissions` VALUES (196,331);
INSERT INTO `rbac_linked_permissions` VALUES (196,335);
INSERT INTO `rbac_linked_permissions` VALUES (196,336);
INSERT INTO `rbac_linked_permissions` VALUES (196,337);
INSERT INTO `rbac_linked_permissions` VALUES (196,339);
INSERT INTO `rbac_linked_permissions` VALUES (196,340);
INSERT INTO `rbac_linked_permissions` VALUES (196,341);
INSERT INTO `rbac_linked_permissions` VALUES (196,342);
INSERT INTO `rbac_linked_permissions` VALUES (196,343);
INSERT INTO `rbac_linked_permissions` VALUES (196,344);
INSERT INTO `rbac_linked_permissions` VALUES (196,345);
INSERT INTO `rbac_linked_permissions` VALUES (196,346);
INSERT INTO `rbac_linked_permissions` VALUES (196,347);
INSERT INTO `rbac_linked_permissions` VALUES (196,348);
INSERT INTO `rbac_linked_permissions` VALUES (196,349);
INSERT INTO `rbac_linked_permissions` VALUES (196,350);
INSERT INTO `rbac_linked_permissions` VALUES (196,351);
INSERT INTO `rbac_linked_permissions` VALUES (196,352);
INSERT INTO `rbac_linked_permissions` VALUES (196,353);
INSERT INTO `rbac_linked_permissions` VALUES (196,354);
INSERT INTO `rbac_linked_permissions` VALUES (196,355);
INSERT INTO `rbac_linked_permissions` VALUES (196,356);
INSERT INTO `rbac_linked_permissions` VALUES (196,357);
INSERT INTO `rbac_linked_permissions` VALUES (196,358);
INSERT INTO `rbac_linked_permissions` VALUES (196,359);
INSERT INTO `rbac_linked_permissions` VALUES (196,360);
INSERT INTO `rbac_linked_permissions` VALUES (196,361);
INSERT INTO `rbac_linked_permissions` VALUES (196,362);
INSERT INTO `rbac_linked_permissions` VALUES (196,363);
INSERT INTO `rbac_linked_permissions` VALUES (196,364);
INSERT INTO `rbac_linked_permissions` VALUES (196,365);
INSERT INTO `rbac_linked_permissions` VALUES (196,366);
INSERT INTO `rbac_linked_permissions` VALUES (196,373);
INSERT INTO `rbac_linked_permissions` VALUES (196,375);
INSERT INTO `rbac_linked_permissions` VALUES (196,400);
INSERT INTO `rbac_linked_permissions` VALUES (196,401);
INSERT INTO `rbac_linked_permissions` VALUES (196,402);
INSERT INTO `rbac_linked_permissions` VALUES (196,403);
INSERT INTO `rbac_linked_permissions` VALUES (196,404);
INSERT INTO `rbac_linked_permissions` VALUES (196,405);
INSERT INTO `rbac_linked_permissions` VALUES (196,406);
INSERT INTO `rbac_linked_permissions` VALUES (196,407);
INSERT INTO `rbac_linked_permissions` VALUES (196,417);
INSERT INTO `rbac_linked_permissions` VALUES (196,418);
INSERT INTO `rbac_linked_permissions` VALUES (196,419);
INSERT INTO `rbac_linked_permissions` VALUES (196,420);
INSERT INTO `rbac_linked_permissions` VALUES (196,421);
INSERT INTO `rbac_linked_permissions` VALUES (196,422);
INSERT INTO `rbac_linked_permissions` VALUES (196,423);
INSERT INTO `rbac_linked_permissions` VALUES (196,424);
INSERT INTO `rbac_linked_permissions` VALUES (196,425);
INSERT INTO `rbac_linked_permissions` VALUES (196,426);
INSERT INTO `rbac_linked_permissions` VALUES (196,427);
INSERT INTO `rbac_linked_permissions` VALUES (196,428);
INSERT INTO `rbac_linked_permissions` VALUES (196,429);
INSERT INTO `rbac_linked_permissions` VALUES (196,434);
INSERT INTO `rbac_linked_permissions` VALUES (196,435);
INSERT INTO `rbac_linked_permissions` VALUES (196,436);
INSERT INTO `rbac_linked_permissions` VALUES (196,437);
INSERT INTO `rbac_linked_permissions` VALUES (196,438);
INSERT INTO `rbac_linked_permissions` VALUES (196,439);
INSERT INTO `rbac_linked_permissions` VALUES (196,440);
INSERT INTO `rbac_linked_permissions` VALUES (196,441);
INSERT INTO `rbac_linked_permissions` VALUES (196,442);
INSERT INTO `rbac_linked_permissions` VALUES (196,443);
INSERT INTO `rbac_linked_permissions` VALUES (196,444);
INSERT INTO `rbac_linked_permissions` VALUES (196,445);
INSERT INTO `rbac_linked_permissions` VALUES (196,446);
INSERT INTO `rbac_linked_permissions` VALUES (196,447);
INSERT INTO `rbac_linked_permissions` VALUES (196,448);
INSERT INTO `rbac_linked_permissions` VALUES (196,449);
INSERT INTO `rbac_linked_permissions` VALUES (196,450);
INSERT INTO `rbac_linked_permissions` VALUES (196,451);
INSERT INTO `rbac_linked_permissions` VALUES (196,452);
INSERT INTO `rbac_linked_permissions` VALUES (196,453);
INSERT INTO `rbac_linked_permissions` VALUES (196,454);
INSERT INTO `rbac_linked_permissions` VALUES (196,455);
INSERT INTO `rbac_linked_permissions` VALUES (196,456);
INSERT INTO `rbac_linked_permissions` VALUES (196,457);
INSERT INTO `rbac_linked_permissions` VALUES (196,458);
INSERT INTO `rbac_linked_permissions` VALUES (196,459);
INSERT INTO `rbac_linked_permissions` VALUES (196,460);
INSERT INTO `rbac_linked_permissions` VALUES (196,461);
INSERT INTO `rbac_linked_permissions` VALUES (196,463);
INSERT INTO `rbac_linked_permissions` VALUES (196,464);
INSERT INTO `rbac_linked_permissions` VALUES (196,465);
INSERT INTO `rbac_linked_permissions` VALUES (196,472);
INSERT INTO `rbac_linked_permissions` VALUES (196,473);
INSERT INTO `rbac_linked_permissions` VALUES (196,474);
INSERT INTO `rbac_linked_permissions` VALUES (196,475);
INSERT INTO `rbac_linked_permissions` VALUES (196,476);
INSERT INTO `rbac_linked_permissions` VALUES (196,477);
INSERT INTO `rbac_linked_permissions` VALUES (196,478);
INSERT INTO `rbac_linked_permissions` VALUES (196,488);
INSERT INTO `rbac_linked_permissions` VALUES (196,489);
INSERT INTO `rbac_linked_permissions` VALUES (196,491);
INSERT INTO `rbac_linked_permissions` VALUES (196,492);
INSERT INTO `rbac_linked_permissions` VALUES (196,493);
INSERT INTO `rbac_linked_permissions` VALUES (196,495);
INSERT INTO `rbac_linked_permissions` VALUES (196,497);
INSERT INTO `rbac_linked_permissions` VALUES (196,498);
INSERT INTO `rbac_linked_permissions` VALUES (196,499);
INSERT INTO `rbac_linked_permissions` VALUES (196,500);
INSERT INTO `rbac_linked_permissions` VALUES (196,502);
INSERT INTO `rbac_linked_permissions` VALUES (196,503);
INSERT INTO `rbac_linked_permissions` VALUES (196,505);
INSERT INTO `rbac_linked_permissions` VALUES (196,508);
INSERT INTO `rbac_linked_permissions` VALUES (196,511);
INSERT INTO `rbac_linked_permissions` VALUES (196,513);
INSERT INTO `rbac_linked_permissions` VALUES (196,514);
INSERT INTO `rbac_linked_permissions` VALUES (196,516);
INSERT INTO `rbac_linked_permissions` VALUES (196,519);
INSERT INTO `rbac_linked_permissions` VALUES (196,522);
INSERT INTO `rbac_linked_permissions` VALUES (196,523);
INSERT INTO `rbac_linked_permissions` VALUES (196,526);
INSERT INTO `rbac_linked_permissions` VALUES (196,527);
INSERT INTO `rbac_linked_permissions` VALUES (196,529);
INSERT INTO `rbac_linked_permissions` VALUES (196,530);
INSERT INTO `rbac_linked_permissions` VALUES (196,533);
INSERT INTO `rbac_linked_permissions` VALUES (196,535);
INSERT INTO `rbac_linked_permissions` VALUES (196,536);
INSERT INTO `rbac_linked_permissions` VALUES (196,537);
INSERT INTO `rbac_linked_permissions` VALUES (196,538);
INSERT INTO `rbac_linked_permissions` VALUES (196,539);
INSERT INTO `rbac_linked_permissions` VALUES (196,540);
INSERT INTO `rbac_linked_permissions` VALUES (196,541);
INSERT INTO `rbac_linked_permissions` VALUES (196,556);
INSERT INTO `rbac_linked_permissions` VALUES (196,581);
INSERT INTO `rbac_linked_permissions` VALUES (196,582);
INSERT INTO `rbac_linked_permissions` VALUES (196,592);
INSERT INTO `rbac_linked_permissions` VALUES (196,593);
INSERT INTO `rbac_linked_permissions` VALUES (196,596);
INSERT INTO `rbac_linked_permissions` VALUES (196,602);
INSERT INTO `rbac_linked_permissions` VALUES (196,603);
INSERT INTO `rbac_linked_permissions` VALUES (196,604);
INSERT INTO `rbac_linked_permissions` VALUES (196,605);
INSERT INTO `rbac_linked_permissions` VALUES (196,606);
INSERT INTO `rbac_linked_permissions` VALUES (196,607);
INSERT INTO `rbac_linked_permissions` VALUES (196,608);
INSERT INTO `rbac_linked_permissions` VALUES (196,609);
INSERT INTO `rbac_linked_permissions` VALUES (196,610);
INSERT INTO `rbac_linked_permissions` VALUES (196,611);
INSERT INTO `rbac_linked_permissions` VALUES (196,612);
INSERT INTO `rbac_linked_permissions` VALUES (196,613);
INSERT INTO `rbac_linked_permissions` VALUES (196,615);
INSERT INTO `rbac_linked_permissions` VALUES (196,616);
INSERT INTO `rbac_linked_permissions` VALUES (196,617);
INSERT INTO `rbac_linked_permissions` VALUES (196,618);
INSERT INTO `rbac_linked_permissions` VALUES (196,619);
INSERT INTO `rbac_linked_permissions` VALUES (196,620);
INSERT INTO `rbac_linked_permissions` VALUES (196,621);
INSERT INTO `rbac_linked_permissions` VALUES (196,623);
INSERT INTO `rbac_linked_permissions` VALUES (196,624);
INSERT INTO `rbac_linked_permissions` VALUES (196,625);
INSERT INTO `rbac_linked_permissions` VALUES (196,626);
INSERT INTO `rbac_linked_permissions` VALUES (196,627);
INSERT INTO `rbac_linked_permissions` VALUES (196,628);
INSERT INTO `rbac_linked_permissions` VALUES (196,629);
INSERT INTO `rbac_linked_permissions` VALUES (196,630);
INSERT INTO `rbac_linked_permissions` VALUES (196,631);
INSERT INTO `rbac_linked_permissions` VALUES (196,633);
INSERT INTO `rbac_linked_permissions` VALUES (196,634);
INSERT INTO `rbac_linked_permissions` VALUES (196,635);
INSERT INTO `rbac_linked_permissions` VALUES (196,636);
INSERT INTO `rbac_linked_permissions` VALUES (196,637);
INSERT INTO `rbac_linked_permissions` VALUES (196,638);
INSERT INTO `rbac_linked_permissions` VALUES (196,639);
INSERT INTO `rbac_linked_permissions` VALUES (196,640);
INSERT INTO `rbac_linked_permissions` VALUES (196,641);
INSERT INTO `rbac_linked_permissions` VALUES (196,642);
INSERT INTO `rbac_linked_permissions` VALUES (196,643);
INSERT INTO `rbac_linked_permissions` VALUES (196,644);
INSERT INTO `rbac_linked_permissions` VALUES (196,645);
INSERT INTO `rbac_linked_permissions` VALUES (196,646);
INSERT INTO `rbac_linked_permissions` VALUES (196,647);
INSERT INTO `rbac_linked_permissions` VALUES (196,648);
INSERT INTO `rbac_linked_permissions` VALUES (196,649);
INSERT INTO `rbac_linked_permissions` VALUES (196,650);
INSERT INTO `rbac_linked_permissions` VALUES (196,651);
INSERT INTO `rbac_linked_permissions` VALUES (196,652);
INSERT INTO `rbac_linked_permissions` VALUES (196,653);
INSERT INTO `rbac_linked_permissions` VALUES (196,654);
INSERT INTO `rbac_linked_permissions` VALUES (196,655);
INSERT INTO `rbac_linked_permissions` VALUES (196,656);
INSERT INTO `rbac_linked_permissions` VALUES (196,657);
INSERT INTO `rbac_linked_permissions` VALUES (196,658);
INSERT INTO `rbac_linked_permissions` VALUES (196,659);
INSERT INTO `rbac_linked_permissions` VALUES (196,660);
INSERT INTO `rbac_linked_permissions` VALUES (196,661);
INSERT INTO `rbac_linked_permissions` VALUES (196,663);
INSERT INTO `rbac_linked_permissions` VALUES (196,665);
INSERT INTO `rbac_linked_permissions` VALUES (196,666);
INSERT INTO `rbac_linked_permissions` VALUES (196,667);
INSERT INTO `rbac_linked_permissions` VALUES (196,668);
INSERT INTO `rbac_linked_permissions` VALUES (196,669);
INSERT INTO `rbac_linked_permissions` VALUES (196,670);
INSERT INTO `rbac_linked_permissions` VALUES (196,671);
INSERT INTO `rbac_linked_permissions` VALUES (196,672);
INSERT INTO `rbac_linked_permissions` VALUES (196,673);
INSERT INTO `rbac_linked_permissions` VALUES (196,674);
INSERT INTO `rbac_linked_permissions` VALUES (196,675);
INSERT INTO `rbac_linked_permissions` VALUES (196,676);
INSERT INTO `rbac_linked_permissions` VALUES (196,677);
INSERT INTO `rbac_linked_permissions` VALUES (196,678);
INSERT INTO `rbac_linked_permissions` VALUES (196,679);
INSERT INTO `rbac_linked_permissions` VALUES (196,680);
INSERT INTO `rbac_linked_permissions` VALUES (196,681);
INSERT INTO `rbac_linked_permissions` VALUES (196,682);
INSERT INTO `rbac_linked_permissions` VALUES (196,683);
INSERT INTO `rbac_linked_permissions` VALUES (196,684);
INSERT INTO `rbac_linked_permissions` VALUES (196,685);
INSERT INTO `rbac_linked_permissions` VALUES (196,686);
INSERT INTO `rbac_linked_permissions` VALUES (196,687);
INSERT INTO `rbac_linked_permissions` VALUES (196,688);
INSERT INTO `rbac_linked_permissions` VALUES (196,689);
INSERT INTO `rbac_linked_permissions` VALUES (196,690);
INSERT INTO `rbac_linked_permissions` VALUES (196,691);
INSERT INTO `rbac_linked_permissions` VALUES (196,693);
INSERT INTO `rbac_linked_permissions` VALUES (196,694);
INSERT INTO `rbac_linked_permissions` VALUES (196,695);
INSERT INTO `rbac_linked_permissions` VALUES (196,696);
INSERT INTO `rbac_linked_permissions` VALUES (196,697);
INSERT INTO `rbac_linked_permissions` VALUES (196,698);
INSERT INTO `rbac_linked_permissions` VALUES (196,699);
INSERT INTO `rbac_linked_permissions` VALUES (196,701);
INSERT INTO `rbac_linked_permissions` VALUES (196,702);
INSERT INTO `rbac_linked_permissions` VALUES (196,703);
INSERT INTO `rbac_linked_permissions` VALUES (196,704);
INSERT INTO `rbac_linked_permissions` VALUES (196,707);
INSERT INTO `rbac_linked_permissions` VALUES (196,708);
INSERT INTO `rbac_linked_permissions` VALUES (196,709);
INSERT INTO `rbac_linked_permissions` VALUES (196,710);
INSERT INTO `rbac_linked_permissions` VALUES (196,711);
INSERT INTO `rbac_linked_permissions` VALUES (196,712);
INSERT INTO `rbac_linked_permissions` VALUES (196,713);
INSERT INTO `rbac_linked_permissions` VALUES (196,714);
INSERT INTO `rbac_linked_permissions` VALUES (196,715);
INSERT INTO `rbac_linked_permissions` VALUES (196,716);
INSERT INTO `rbac_linked_permissions` VALUES (196,717);
INSERT INTO `rbac_linked_permissions` VALUES (196,718);
INSERT INTO `rbac_linked_permissions` VALUES (196,719);
INSERT INTO `rbac_linked_permissions` VALUES (196,721);
INSERT INTO `rbac_linked_permissions` VALUES (196,722);
INSERT INTO `rbac_linked_permissions` VALUES (196,723);
INSERT INTO `rbac_linked_permissions` VALUES (196,724);
INSERT INTO `rbac_linked_permissions` VALUES (196,725);
INSERT INTO `rbac_linked_permissions` VALUES (196,726);
INSERT INTO `rbac_linked_permissions` VALUES (196,727);
INSERT INTO `rbac_linked_permissions` VALUES (196,728);
INSERT INTO `rbac_linked_permissions` VALUES (196,729);
INSERT INTO `rbac_linked_permissions` VALUES (196,730);
INSERT INTO `rbac_linked_permissions` VALUES (196,733);
INSERT INTO `rbac_linked_permissions` VALUES (196,734);
INSERT INTO `rbac_linked_permissions` VALUES (196,735);
INSERT INTO `rbac_linked_permissions` VALUES (196,736);
INSERT INTO `rbac_linked_permissions` VALUES (196,738);
INSERT INTO `rbac_linked_permissions` VALUES (196,739);
INSERT INTO `rbac_linked_permissions` VALUES (196,753);
INSERT INTO `rbac_linked_permissions` VALUES (196,757);
INSERT INTO `rbac_linked_permissions` VALUES (196,773);
INSERT INTO `rbac_linked_permissions` VALUES (196,777);
INSERT INTO `rbac_linked_permissions` VALUES (196,809);
INSERT INTO `rbac_linked_permissions` VALUES (196,817);
INSERT INTO `rbac_linked_permissions` VALUES (196,825);
INSERT INTO `rbac_linked_permissions` VALUES (196,829);
INSERT INTO `rbac_linked_permissions` VALUES (196,830);
INSERT INTO `rbac_linked_permissions` VALUES (196,831);
INSERT INTO `rbac_linked_permissions` VALUES (196,832);
INSERT INTO `rbac_linked_permissions` VALUES (196,833);
INSERT INTO `rbac_linked_permissions` VALUES (196,836);
INSERT INTO `rbac_linked_permissions` VALUES (196,837);
INSERT INTO `rbac_linked_permissions` VALUES (196,838);
INSERT INTO `rbac_linked_permissions` VALUES (196,839);
INSERT INTO `rbac_linked_permissions` VALUES (196,840);
INSERT INTO `rbac_linked_permissions` VALUES (196,842);
INSERT INTO `rbac_linked_permissions` VALUES (196,843);
INSERT INTO `rbac_linked_permissions` VALUES (196,852);
INSERT INTO `rbac_linked_permissions` VALUES (196,866);
INSERT INTO `rbac_linked_permissions` VALUES (196,869);
INSERT INTO `rbac_linked_permissions` VALUES (196,870);
INSERT INTO `rbac_linked_permissions` VALUES (196,871);
INSERT INTO `rbac_linked_permissions` VALUES (196,872);
INSERT INTO `rbac_linked_permissions` VALUES (196,873);
INSERT INTO `rbac_linked_permissions` VALUES (196,875);
INSERT INTO `rbac_linked_permissions` VALUES (196,876);
INSERT INTO `rbac_linked_permissions` VALUES (196,877);
INSERT INTO `rbac_linked_permissions` VALUES (196,878);
INSERT INTO `rbac_linked_permissions` VALUES (196,879);
INSERT INTO `rbac_linked_permissions` VALUES (196,880);
INSERT INTO `rbac_linked_permissions` VALUES (196,881);
INSERT INTO `rbac_linked_permissions` VALUES (196,882);
INSERT INTO `rbac_linked_permissions` VALUES (196,883);
INSERT INTO `rbac_linked_permissions` VALUES (197,232);
INSERT INTO `rbac_linked_permissions` VALUES (197,236);
INSERT INTO `rbac_linked_permissions` VALUES (197,237);
INSERT INTO `rbac_linked_permissions` VALUES (197,273);
INSERT INTO `rbac_linked_permissions` VALUES (197,274);
INSERT INTO `rbac_linked_permissions` VALUES (197,275);
INSERT INTO `rbac_linked_permissions` VALUES (197,276);
INSERT INTO `rbac_linked_permissions` VALUES (197,277);
INSERT INTO `rbac_linked_permissions` VALUES (197,284);
INSERT INTO `rbac_linked_permissions` VALUES (197,285);
INSERT INTO `rbac_linked_permissions` VALUES (197,286);
INSERT INTO `rbac_linked_permissions` VALUES (197,301);
INSERT INTO `rbac_linked_permissions` VALUES (197,311);
INSERT INTO `rbac_linked_permissions` VALUES (197,387);
INSERT INTO `rbac_linked_permissions` VALUES (197,388);
INSERT INTO `rbac_linked_permissions` VALUES (197,389);
INSERT INTO `rbac_linked_permissions` VALUES (197,390);
INSERT INTO `rbac_linked_permissions` VALUES (197,391);
INSERT INTO `rbac_linked_permissions` VALUES (197,392);
INSERT INTO `rbac_linked_permissions` VALUES (197,393);
INSERT INTO `rbac_linked_permissions` VALUES (197,394);
INSERT INTO `rbac_linked_permissions` VALUES (197,395);
INSERT INTO `rbac_linked_permissions` VALUES (197,396);
INSERT INTO `rbac_linked_permissions` VALUES (197,397);
INSERT INTO `rbac_linked_permissions` VALUES (197,398);
INSERT INTO `rbac_linked_permissions` VALUES (197,399);
INSERT INTO `rbac_linked_permissions` VALUES (197,479);
INSERT INTO `rbac_linked_permissions` VALUES (197,480);
INSERT INTO `rbac_linked_permissions` VALUES (197,481);
INSERT INTO `rbac_linked_permissions` VALUES (197,482);
INSERT INTO `rbac_linked_permissions` VALUES (197,485);
INSERT INTO `rbac_linked_permissions` VALUES (197,486);
INSERT INTO `rbac_linked_permissions` VALUES (197,487);
INSERT INTO `rbac_linked_permissions` VALUES (197,494);
INSERT INTO `rbac_linked_permissions` VALUES (197,501);
INSERT INTO `rbac_linked_permissions` VALUES (197,506);
INSERT INTO `rbac_linked_permissions` VALUES (197,509);
INSERT INTO `rbac_linked_permissions` VALUES (197,510);
INSERT INTO `rbac_linked_permissions` VALUES (197,517);
INSERT INTO `rbac_linked_permissions` VALUES (197,518);
INSERT INTO `rbac_linked_permissions` VALUES (197,521);
INSERT INTO `rbac_linked_permissions` VALUES (197,542);
INSERT INTO `rbac_linked_permissions` VALUES (197,543);
INSERT INTO `rbac_linked_permissions` VALUES (197,550);
INSERT INTO `rbac_linked_permissions` VALUES (197,558);
INSERT INTO `rbac_linked_permissions` VALUES (197,568);
INSERT INTO `rbac_linked_permissions` VALUES (197,571);
INSERT INTO `rbac_linked_permissions` VALUES (197,572);
INSERT INTO `rbac_linked_permissions` VALUES (197,573);
INSERT INTO `rbac_linked_permissions` VALUES (197,574);
INSERT INTO `rbac_linked_permissions` VALUES (197,575);
INSERT INTO `rbac_linked_permissions` VALUES (197,576);
INSERT INTO `rbac_linked_permissions` VALUES (197,577);
INSERT INTO `rbac_linked_permissions` VALUES (197,578);
INSERT INTO `rbac_linked_permissions` VALUES (197,579);
INSERT INTO `rbac_linked_permissions` VALUES (197,580);
INSERT INTO `rbac_linked_permissions` VALUES (197,583);
INSERT INTO `rbac_linked_permissions` VALUES (197,584);
INSERT INTO `rbac_linked_permissions` VALUES (197,585);
INSERT INTO `rbac_linked_permissions` VALUES (197,586);
INSERT INTO `rbac_linked_permissions` VALUES (197,587);
INSERT INTO `rbac_linked_permissions` VALUES (197,588);
INSERT INTO `rbac_linked_permissions` VALUES (197,589);
INSERT INTO `rbac_linked_permissions` VALUES (197,590);
INSERT INTO `rbac_linked_permissions` VALUES (197,591);
INSERT INTO `rbac_linked_permissions` VALUES (197,594);
INSERT INTO `rbac_linked_permissions` VALUES (197,595);
INSERT INTO `rbac_linked_permissions` VALUES (197,601);
INSERT INTO `rbac_linked_permissions` VALUES (197,761);
INSERT INTO `rbac_linked_permissions` VALUES (197,762);
INSERT INTO `rbac_linked_permissions` VALUES (197,763);
INSERT INTO `rbac_linked_permissions` VALUES (197,764);
INSERT INTO `rbac_linked_permissions` VALUES (197,765);
INSERT INTO `rbac_linked_permissions` VALUES (197,766);
INSERT INTO `rbac_linked_permissions` VALUES (197,767);
INSERT INTO `rbac_linked_permissions` VALUES (197,768);
INSERT INTO `rbac_linked_permissions` VALUES (197,769);
INSERT INTO `rbac_linked_permissions` VALUES (197,770);
INSERT INTO `rbac_linked_permissions` VALUES (197,771);
INSERT INTO `rbac_linked_permissions` VALUES (197,772);
INSERT INTO `rbac_linked_permissions` VALUES (197,774);
INSERT INTO `rbac_linked_permissions` VALUES (197,775);
INSERT INTO `rbac_linked_permissions` VALUES (197,805);
INSERT INTO `rbac_linked_permissions` VALUES (197,811);
INSERT INTO `rbac_linked_permissions` VALUES (197,813);
INSERT INTO `rbac_linked_permissions` VALUES (197,819);
INSERT INTO `rbac_linked_permissions` VALUES (197,821);
INSERT INTO `rbac_linked_permissions` VALUES (197,827);
INSERT INTO `rbac_linked_permissions` VALUES (197,856);
INSERT INTO `rbac_linked_permissions` VALUES (197,857);
INSERT INTO `rbac_linked_permissions` VALUES (197,858);
INSERT INTO `rbac_linked_permissions` VALUES (197,859);
INSERT INTO `rbac_linked_permissions` VALUES (197,860);
INSERT INTO `rbac_linked_permissions` VALUES (197,865);
INSERT INTO `rbac_linked_permissions` VALUES (197,884);
INSERT INTO `rbac_linked_permissions` VALUES (197,885);
INSERT INTO `rbac_linked_permissions` VALUES (198,218);
INSERT INTO `rbac_linked_permissions` VALUES (198,300);
INSERT INTO `rbac_linked_permissions` VALUES (198,312);
INSERT INTO `rbac_linked_permissions` VALUES (198,315);
INSERT INTO `rbac_linked_permissions` VALUES (198,316);
INSERT INTO `rbac_linked_permissions` VALUES (198,317);
INSERT INTO `rbac_linked_permissions` VALUES (198,318);
INSERT INTO `rbac_linked_permissions` VALUES (198,367);
INSERT INTO `rbac_linked_permissions` VALUES (198,368);
INSERT INTO `rbac_linked_permissions` VALUES (198,369);
INSERT INTO `rbac_linked_permissions` VALUES (198,370);
INSERT INTO `rbac_linked_permissions` VALUES (198,371);
INSERT INTO `rbac_linked_permissions` VALUES (198,372);
INSERT INTO `rbac_linked_permissions` VALUES (198,374);
INSERT INTO `rbac_linked_permissions` VALUES (198,376);
INSERT INTO `rbac_linked_permissions` VALUES (198,377);
INSERT INTO `rbac_linked_permissions` VALUES (198,408);
INSERT INTO `rbac_linked_permissions` VALUES (198,409);
INSERT INTO `rbac_linked_permissions` VALUES (198,410);
INSERT INTO `rbac_linked_permissions` VALUES (198,411);
INSERT INTO `rbac_linked_permissions` VALUES (198,412);
INSERT INTO `rbac_linked_permissions` VALUES (198,413);
INSERT INTO `rbac_linked_permissions` VALUES (198,414);
INSERT INTO `rbac_linked_permissions` VALUES (198,415);
INSERT INTO `rbac_linked_permissions` VALUES (198,430);
INSERT INTO `rbac_linked_permissions` VALUES (198,431);
INSERT INTO `rbac_linked_permissions` VALUES (198,432);
INSERT INTO `rbac_linked_permissions` VALUES (198,433);
INSERT INTO `rbac_linked_permissions` VALUES (198,462);
INSERT INTO `rbac_linked_permissions` VALUES (198,466);
INSERT INTO `rbac_linked_permissions` VALUES (198,467);
INSERT INTO `rbac_linked_permissions` VALUES (198,468);
INSERT INTO `rbac_linked_permissions` VALUES (198,469);
INSERT INTO `rbac_linked_permissions` VALUES (198,470);
INSERT INTO `rbac_linked_permissions` VALUES (198,471);
INSERT INTO `rbac_linked_permissions` VALUES (198,483);
INSERT INTO `rbac_linked_permissions` VALUES (198,484);
INSERT INTO `rbac_linked_permissions` VALUES (198,490);
INSERT INTO `rbac_linked_permissions` VALUES (198,504);
INSERT INTO `rbac_linked_permissions` VALUES (198,512);
INSERT INTO `rbac_linked_permissions` VALUES (198,515);
INSERT INTO `rbac_linked_permissions` VALUES (198,520);
INSERT INTO `rbac_linked_permissions` VALUES (198,524);
INSERT INTO `rbac_linked_permissions` VALUES (198,528);
INSERT INTO `rbac_linked_permissions` VALUES (198,531);
INSERT INTO `rbac_linked_permissions` VALUES (198,532);
INSERT INTO `rbac_linked_permissions` VALUES (198,544);
INSERT INTO `rbac_linked_permissions` VALUES (198,545);
INSERT INTO `rbac_linked_permissions` VALUES (198,547);
INSERT INTO `rbac_linked_permissions` VALUES (198,548);
INSERT INTO `rbac_linked_permissions` VALUES (198,549);
INSERT INTO `rbac_linked_permissions` VALUES (198,551);
INSERT INTO `rbac_linked_permissions` VALUES (198,552);
INSERT INTO `rbac_linked_permissions` VALUES (198,553);
INSERT INTO `rbac_linked_permissions` VALUES (198,554);
INSERT INTO `rbac_linked_permissions` VALUES (198,555);
INSERT INTO `rbac_linked_permissions` VALUES (198,557);
INSERT INTO `rbac_linked_permissions` VALUES (198,559);
INSERT INTO `rbac_linked_permissions` VALUES (198,560);
INSERT INTO `rbac_linked_permissions` VALUES (198,561);
INSERT INTO `rbac_linked_permissions` VALUES (198,562);
INSERT INTO `rbac_linked_permissions` VALUES (198,563);
INSERT INTO `rbac_linked_permissions` VALUES (198,564);
INSERT INTO `rbac_linked_permissions` VALUES (198,565);
INSERT INTO `rbac_linked_permissions` VALUES (198,566);
INSERT INTO `rbac_linked_permissions` VALUES (198,567);
INSERT INTO `rbac_linked_permissions` VALUES (198,569);
INSERT INTO `rbac_linked_permissions` VALUES (198,570);
INSERT INTO `rbac_linked_permissions` VALUES (198,597);
INSERT INTO `rbac_linked_permissions` VALUES (198,598);
INSERT INTO `rbac_linked_permissions` VALUES (198,599);
INSERT INTO `rbac_linked_permissions` VALUES (198,600);
INSERT INTO `rbac_linked_permissions` VALUES (198,737);
INSERT INTO `rbac_linked_permissions` VALUES (198,740);
INSERT INTO `rbac_linked_permissions` VALUES (198,741);
INSERT INTO `rbac_linked_permissions` VALUES (198,742);
INSERT INTO `rbac_linked_permissions` VALUES (198,802);
INSERT INTO `rbac_linked_permissions` VALUES (198,803);
INSERT INTO `rbac_linked_permissions` VALUES (198,804);
INSERT INTO `rbac_linked_permissions` VALUES (198,806);
INSERT INTO `rbac_linked_permissions` VALUES (198,807);
INSERT INTO `rbac_linked_permissions` VALUES (198,808);
INSERT INTO `rbac_linked_permissions` VALUES (198,810);
INSERT INTO `rbac_linked_permissions` VALUES (198,812);
INSERT INTO `rbac_linked_permissions` VALUES (198,814);
INSERT INTO `rbac_linked_permissions` VALUES (198,815);
INSERT INTO `rbac_linked_permissions` VALUES (198,816);
INSERT INTO `rbac_linked_permissions` VALUES (198,818);
INSERT INTO `rbac_linked_permissions` VALUES (198,820);
INSERT INTO `rbac_linked_permissions` VALUES (198,822);
INSERT INTO `rbac_linked_permissions` VALUES (198,823);
INSERT INTO `rbac_linked_permissions` VALUES (198,824);
INSERT INTO `rbac_linked_permissions` VALUES (198,826);
INSERT INTO `rbac_linked_permissions` VALUES (198,828);
INSERT INTO `rbac_linked_permissions` VALUES (198,855);
INSERT INTO `rbac_linked_permissions` VALUES (198,868);
INSERT INTO `rbac_linked_permissions` VALUES (199,207);
INSERT INTO `rbac_linked_permissions` VALUES (199,209);
INSERT INTO `rbac_linked_permissions` VALUES (199,210);
INSERT INTO `rbac_linked_permissions` VALUES (199,211);
INSERT INTO `rbac_linked_permissions` VALUES (199,217);
INSERT INTO `rbac_linked_permissions` VALUES (199,221);
INSERT INTO `rbac_linked_permissions` VALUES (199,222);
INSERT INTO `rbac_linked_permissions` VALUES (199,223);
INSERT INTO `rbac_linked_permissions` VALUES (199,225);
INSERT INTO `rbac_linked_permissions` VALUES (199,263);
INSERT INTO `rbac_linked_permissions` VALUES (199,378);
INSERT INTO `rbac_linked_permissions` VALUES (199,379);
INSERT INTO `rbac_linked_permissions` VALUES (199,380);
INSERT INTO `rbac_linked_permissions` VALUES (199,496);
INSERT INTO `rbac_linked_permissions` VALUES (199,507);
INSERT INTO `rbac_linked_permissions` VALUES (199,525);
INSERT INTO `rbac_linked_permissions` VALUES (199,534);
INSERT INTO `rbac_linked_permissions` VALUES (199,797);
/*!40000 ALTER TABLE `rbac_linked_permissions` ENABLE KEYS */;

/*!40000 ALTER TABLE `rbac_default_permissions` DISABLE KEYS */;
INSERT INTO `rbac_default_permissions` VALUES (3,192,-1);
INSERT INTO `rbac_default_permissions` VALUES (2,193,-1);
INSERT INTO `rbac_default_permissions` VALUES (1,194,-1);
INSERT INTO `rbac_default_permissions` VALUES (0,195,-1);
/*!40000 ALTER TABLE `rbac_default_permissions` ENABLE KEYS */;

/*!40000 ALTER TABLE `realmlist` DISABLE KEYS */;
INSERT INTO `realmlist` VALUES (70,'Normal','127.0.0.1','127.0.0.1',NULL,NULL,'255.255.255.0',8085,0,0,1,0,0,70425,70,1,137,0);
INSERT INTO `realmlist` VALUES (71,'PvP','127.0.0.1','127.0.0.1',NULL,NULL,'255.255.255.0',8095,1,0,1,0,0,70425,70,1,136,0);
INSERT INTO `realmlist` VALUES (72,'RP','127.0.0.1','127.0.0.1',NULL,NULL,'255.255.255.0',8105,6,0,1,0,0,70425,70,1,138,0);
INSERT INTO `realmlist` VALUES (73,'Hardcore','127.0.0.1','127.0.0.1',NULL,NULL,'255.255.255.0',8115,0,0,1,0,0,70425,70,1,140,1);
/*!40000 ALTER TABLE `realmlist` ENABLE KEYS */;
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;


SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS;
