
# Introduction 
Simple console application that monitors my Uphold altcoin portfolio and optionally notifies me when a given altcoin has reached a trade target. Notifications can be sent to Slack and Pushbullet. Transactions can be logged to Google Sheets.

# Getting Started
This application requires a MySQL/MariaDB database. The file [Database scripts.sql](https://github.com/briankavanaugh/CryptoWatch/blob/main/Database%20scripts.sql) will create the tables and views used. There is no UI, so modifying any of the settings stored in the table CryptoCurrency must be done directly. I use [HeidiSQL](https://www.heidisql.com/).

I use [Uphold](https://uphold.com/), so the transaction file format expected is what they use. Note that there may be some symbols that differ between Uphold and CoinMarketCap. One example is Bitcoin Zero (BTC0 versus BTZ). The CryptoCurrency.AltSymbol field is where you put the symbol CoinMarketCap uses. Also, there may be altcoins that CoinMarketCap does not support (e.g., Universal Carbon, UPCO2). these **must** be marked excluded (CryptoCurrency.Exclude = 1), or your API calls to CoinMarketCap will fail.

Uphold supports limit orders now, so those are calculated and displayed. To control the thresholds for buys and sells, modify CryptoCurrency.BalanceTarget, .BuyTarget and .SellTarget.

A subscription to [CoinMarketCap Pro API](https://pro.coinmarketcap.com/api/v1) is required. The free version should be enough. The refresh and do not disturb settings are such that you shouldn't exceed the daily or monthly limits (see [appSettings.json](https://github.com/briankavanaugh/CryptoWatch/blob/main/CryptoWatch/appSettings.json)/General). Note that it is configured to run against their sandbox if compiled in debug mode.

This is using a modified version of [CoinMarketCap API Client](https://github.com/lzehrung/coinmarketcap), so that multiple symbols can be submitted at one time. I've submitted a pull request to address this, but a release including it has not happened yet as of writing this (current version is 2.0.0 from June 17, 2020). Once it has, CryptoWatch.Services can be modified to reference that NuGet package. Until then, my modified version is available [here](https://github.com/briankavanaugh/coinmarketcap).

There is optional support for using Google Sheets to log your transactions. [This article](https://medium.com/@williamchislett/writing-to-google-sheets-api-using-net-and-a-services-account-91ee7e4a291) covers how to set that up. Some notes on how it works:
* The credentials.json file must be in the same directory as the executable.
* You must create a sheet for each symbol ahead of time, named the same as what is stored in CryptoCurrency.Symbol.
* Excluded symbols are not written, except for the cash symbol.
* The cash position will have all transactions listed, assuming all other transactions were either buying or selling cash.
* All other symbols will have a set of columns for all transactions, a set of columns for all buys, and a set of columns for all sells. They start on row two, because I have headers (not written out by this) that sum up the buys and sells and calculate the average price so that I can compare how I am doing.
	*	A1: Balance Sheet
	*	G1: Buys
	*	H1: =I1/J1
	*	I1: =sum(I2:I1000)
	*	J1: =sum(J2:J1000)
	*	L1: Sells
	*	M1: =N1/O1
	*	N1: =sum(N2:N1000)
	*	O1: =sum(O2:O1000)

All sensitive values are stored in environment variables (though you could put them in the appSettings.json file if you wanted to - just don't add that to git). The prefix "Crypto" is used.
* appSettings.json/CoinMarketCap/ApiKey = Crypto_CoinMarketCap__ApiKey
* appSettings.json/ConnectionStrings/Crypto = Crypto_ConnectionStrings__Crypto (database connection string)
* appSettings.json/Integrations/GoogleSheetsId = Crypto_Integrations__GoogleSheetsId (the ID of the Google Sheets spreadsheet to populate)
* appSettings.json/Integrations/PushbulletToken = Crypto_Integrations__PushbulletToken
* appSettings.json/Integrations/Slack = Crypto_Integrations__Slack (Slack webhook URL)

# Contribute
Issues or suggestions? Feel free to contribute by submitting a pull request!

# License
[MIT](https://github.com/briankavanaugh/CryptoWatch/blob/main/LICENSE)

# Target Framework
.NET 5.0