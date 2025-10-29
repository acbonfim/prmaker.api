CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;

START TRANSACTION;

ALTER DATABASE CHARACTER SET utf8mb4;

CREATE TABLE `AspNetRoles` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ExternalId` char(36) COLLATE ascii_general_ci NOT NULL,
    `Name` varchar(256) CHARACTER SET utf8mb4 NULL,
    `NormalizedName` varchar(256) CHARACTER SET utf8mb4 NULL,
    `ConcurrencyStamp` longtext CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_AspNetRoles` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `AspNetUsers` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `FullName` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Departamento` longtext CHARACTER SET utf8mb4 NOT NULL,
    `DataUltimoLogin` datetime(6) NOT NULL,
    `Active` tinyint(1) NOT NULL,
    `ExternalId` char(36) COLLATE ascii_general_ci NOT NULL,
    `CompanyId` int NOT NULL,
    `UserName` varchar(256) CHARACTER SET utf8mb4 NULL,
    `NormalizedUserName` varchar(256) CHARACTER SET utf8mb4 NULL,
    `Email` varchar(256) CHARACTER SET utf8mb4 NULL,
    `NormalizedEmail` varchar(256) CHARACTER SET utf8mb4 NULL,
    `EmailConfirmed` tinyint(1) NOT NULL,
    `PasswordHash` longtext CHARACTER SET utf8mb4 NULL,
    `SecurityStamp` longtext CHARACTER SET utf8mb4 NULL,
    `ConcurrencyStamp` longtext CHARACTER SET utf8mb4 NULL,
    `PhoneNumber` longtext CHARACTER SET utf8mb4 NULL,
    `PhoneNumberConfirmed` tinyint(1) NOT NULL,
    `TwoFactorEnabled` tinyint(1) NOT NULL,
    `LockoutEnd` datetime(6) NULL,
    `LockoutEnabled` tinyint(1) NOT NULL,
    `AccessFailedCount` int NOT NULL,
    CONSTRAINT `PK_AspNetUsers` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `UserForgetCodes` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `UserId` int NOT NULL,
    `ForgetCode` longtext CHARACTER SET utf8mb4 NOT NULL,
    `ExpirationDate` datetime(6) NOT NULL,
    CONSTRAINT `PK_UserForgetCodes` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `AspNetRoleClaims` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RoleId` int NOT NULL,
    `ClaimType` longtext CHARACTER SET utf8mb4 NULL,
    `ClaimValue` longtext CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_AspNetRoleClaims` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_AspNetRoleClaims_AspNetRoles_RoleId` FOREIGN KEY (`RoleId`) REFERENCES `AspNetRoles` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `AspNetUserClaims` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` int NOT NULL,
    `ClaimType` longtext CHARACTER SET utf8mb4 NULL,
    `ClaimValue` longtext CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_AspNetUserClaims` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_AspNetUserClaims_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `AspNetUserLogins` (
    `LoginProvider` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `ProviderKey` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `ProviderDisplayName` longtext CHARACTER SET utf8mb4 NULL,
    `UserId` int NOT NULL,
    CONSTRAINT `PK_AspNetUserLogins` PRIMARY KEY (`LoginProvider`, `ProviderKey`),
    CONSTRAINT `FK_AspNetUserLogins_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `AspNetUserRoles` (
    `UserId` int NOT NULL,
    `RoleId` int NOT NULL,
    CONSTRAINT `PK_AspNetUserRoles` PRIMARY KEY (`UserId`, `RoleId`),
    CONSTRAINT `FK_AspNetUserRoles_AspNetRoles_RoleId` FOREIGN KEY (`RoleId`) REFERENCES `AspNetRoles` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_AspNetUserRoles_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `AspNetUserTokens` (
    `UserId` int NOT NULL,
    `LoginProvider` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `Name` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `Value` longtext CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_AspNetUserTokens` PRIMARY KEY (`UserId`, `LoginProvider`, `Name`),
    CONSTRAINT `FK_AspNetUserTokens_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

INSERT INTO `AspNetRoles` (`Id`, `ConcurrencyStamp`, `ExternalId`, `Name`, `NormalizedName`)
VALUES (1, '88038803-fab9-41c9-9132-2984b083d647', '3ae55e1d-1dcd-4b35-953c-9fb2188232fa', 'admin', 'ADMIN'),
(2, '6252e105-eb4a-4a46-aa80-d028e3d68669', 'db2d0fea-e2b6-4baa-a40a-a0846e1463eb', 'user', 'USER'),
(3, '424120d7-2aae-4023-9db0-bdb41ad9747f', '8de8ae78-bc76-4583-9d19-89ea986d5544', 'external_client', 'EXTERNALCLIENT');

CREATE INDEX `IX_AspNetRoleClaims_RoleId` ON `AspNetRoleClaims` (`RoleId`);

CREATE UNIQUE INDEX `RoleNameIndex` ON `AspNetRoles` (`NormalizedName`);

CREATE INDEX `IX_AspNetUserClaims_UserId` ON `AspNetUserClaims` (`UserId`);

CREATE INDEX `IX_AspNetUserLogins_UserId` ON `AspNetUserLogins` (`UserId`);

CREATE INDEX `IX_AspNetUserRoles_RoleId` ON `AspNetUserRoles` (`RoleId`);

CREATE INDEX `EmailIndex` ON `AspNetUsers` (`NormalizedEmail`);

CREATE UNIQUE INDEX `UserNameIndex` ON `AspNetUsers` (`NormalizedUserName`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20230511171630_init', '7.0.5');

COMMIT;

START TRANSACTION;

ALTER TABLE `AspNetUsers` ADD `ChannelOrigin` longtext CHARACTER SET utf8mb4 NOT NULL;

UPDATE `AspNetRoles` SET `ConcurrencyStamp` = '8DD68F0C-5AA5-40E4-AE26-C93166E4A1C9', `ExternalId` = '82904b6d-bf05-4d2a-a98f-6c665a6a02a4'
WHERE `Id` = 1;
SELECT ROW_COUNT();


UPDATE `AspNetRoles` SET `ConcurrencyStamp` = 'D3AE6011-BA86-4923-A987-76FAD446C46A', `ExternalId` = 'abeb985b-1640-466c-a581-5cf170342827'
WHERE `Id` = 2;
SELECT ROW_COUNT();


UPDATE `AspNetRoles` SET `ConcurrencyStamp` = '8D28F93A-1771-47A9-9657-22798B4FA74C', `ExternalId` = 'c48b559b-db3b-4ec8-a06f-4f5e055c5210'
WHERE `Id` = 3;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20230820210902_AddChannelOrigin', '7.0.5');

COMMIT;

START TRANSACTION;



UPDATE `AspNetRoles` SET `ConcurrencyStamp` = '45F2E010-33EB-4692-8BE7-C042D3CDEB25', `ExternalId` = '11b0bdb7-ccd7-4811-8aa8-93d9a92ebc0b', `Level` = 0
WHERE `Id` = 1;
SELECT ROW_COUNT();


UPDATE `AspNetRoles` SET `ConcurrencyStamp` = 'A160DCA9-5F62-42C2-B03C-9AF6728E2C8B', `ExternalId` = '3de81c66-ae8f-4205-b5cd-eb1485e020a0', `Level` = 0
WHERE `Id` = 2;
SELECT ROW_COUNT();


UPDATE `AspNetRoles` SET `ConcurrencyStamp` = '81997F5A-CB20-4576-9E7B-3F769E69ED99', `ExternalId` = 'a542e938-55a9-4463-b288-8d45c44fac1f', `Level` = 0
WHERE `Id` = 3;
SELECT ROW_COUNT();


INSERT INTO `AspNetRoles` (`Id`, `ConcurrencyStamp`, `ExternalId`, `Level`, `Name`, `NormalizedName`)
VALUES (4, 'F14EA8C7-2A2A-404E-841F-930B0B3CF8F0', 'e3f71561-89a1-4d36-9ef7-0301efc85e18', 0, 'support', 'SUPPORT');



INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20231209000026_AddServices', '7.0.5');

COMMIT;

