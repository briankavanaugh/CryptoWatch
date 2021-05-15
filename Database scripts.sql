-- --------------------------------------------------------
-- Host:                         vitalstatistix.gaulishvillage.home
-- Server version:               10.4.19-MariaDB-1:10.4.19+maria~bionic-log - mariadb.org binary distribution
-- Server OS:                    debian-linux-gnu
-- HeidiSQL Version:             11.2.0.6213
-- --------------------------------------------------------

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET NAMES utf8 */;
/*!50503 SET NAMES utf8mb4 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;


-- Dumping database structure for Crypto
CREATE DATABASE IF NOT EXISTS `Crypto` /*!40100 DEFAULT CHARACTER SET utf8 */;
USE `Crypto`;

-- Dumping structure for view Crypto.Balance
-- Creating temporary table to overcome VIEW dependency errors
CREATE TABLE `Balance` (
	`Id` INT(11) NOT NULL,
	`Symbol` VARCHAR(10) NOT NULL COLLATE 'utf8_general_ci',
	`AltSymbol` VARCHAR(10) NULL COMMENT 'Symbol used on CoinMarketApp, when different' COLLATE 'utf8_general_ci',
	`Name` VARCHAR(50) NULL COLLATE 'utf8_general_ci',
	`Exclude` BIT(1) NOT NULL COMMENT 'When true, exclude from quote requests',
	`Amount` DECIMAL(45,18) NULL,
	`BalanceTarget` DECIMAL(10,2) NOT NULL COMMENT 'Default target balance'
) ENGINE=MyISAM;

-- Dumping structure for table Crypto.CryptoCurrency
CREATE TABLE IF NOT EXISTS `CryptoCurrency` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Symbol` varchar(10) NOT NULL,
  `AltSymbol` varchar(10) DEFAULT NULL COMMENT 'Symbol used on CoinMarketApp, when different',
  `Created` timestamp NOT NULL DEFAULT current_timestamp(),
  `Name` varchar(50) DEFAULT NULL,
  `Slug` varchar(50) DEFAULT NULL,
  `ExternalId` bigint(20) DEFAULT NULL,
  `AddedToExchange` date DEFAULT NULL,
  `Exclude` bit(1) NOT NULL DEFAULT b'0' COMMENT 'When true, exclude from quote requests',
  `BalanceTarget` decimal(10,2) NOT NULL DEFAULT 100.00 COMMENT 'Default target balance',
  PRIMARY KEY (`Id`),
  KEY `Symbol` (`Symbol`)
) ENGINE=InnoDB AUTO_INCREMENT=17 DEFAULT CHARSET=utf8;

-- Data exporting was unselected.

-- Dumping structure for table Crypto.Transaction
CREATE TABLE IF NOT EXISTS `Transaction` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `ExternalId` char(36) NOT NULL COMMENT 'UUID',
  `CryptoCurrencyId` int(11) NOT NULL,
  `Amount` decimal(23,18) NOT NULL COMMENT 'Buy/transfer in is positive, sell/transfer out is negative',
  `Destination` varchar(15) NOT NULL DEFAULT 'uphold',
  `Origin` varchar(15) NOT NULL,
  `Status` varchar(15) NOT NULL,
  `Type` varchar(15) NOT NULL,
  `TransactionDate` datetime NOT NULL,
  `Created` timestamp NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`Id`),
  KEY `FK_Transaction_CryptoCurrency` (`CryptoCurrencyId`),
  CONSTRAINT `FK_Transaction_CryptoCurrency` FOREIGN KEY (`CryptoCurrencyId`) REFERENCES `CryptoCurrency` (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=1009 DEFAULT CHARSET=utf8;

-- Data exporting was unselected.

-- Dumping structure for view Crypto.Balance
-- Removing temporary table and create final VIEW structure
DROP TABLE IF EXISTS `Balance`;
CREATE ALGORITHM=UNDEFINED SQL SECURITY DEFINER VIEW `Balance` AS select `c`.`Id` AS `Id`,`c`.`Symbol` AS `Symbol`,`c`.`AltSymbol` AS `AltSymbol`,`c`.`Name` AS `Name`,`c`.`Exclude` AS `Exclude`,sum(`t`.`Amount`) AS `Amount`,`c`.`BalanceTarget` AS `BalanceTarget` from (`Transaction` `t` join `CryptoCurrency` `c` on(`t`.`CryptoCurrencyId` = `c`.`Id`)) group by `c`.`Id`,`c`.`Symbol`,`c`.`AltSymbol`,`c`.`Name`,`c`.`Exclude`,`c`.`BalanceTarget` having sum(`t`.`Amount`) > 0;

/*!40101 SET SQL_MODE=IFNULL(@OLD_SQL_MODE, '') */;
/*!40014 SET FOREIGN_KEY_CHECKS=IFNULL(@OLD_FOREIGN_KEY_CHECKS, 1) */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40111 SET SQL_NOTES=IFNULL(@OLD_SQL_NOTES, 1) */;
