# Introduction

**Make sure you are in the right project (CryptoWatch.Entities), using an account that has the correct privileges, and remove OnConfiguring from the context when done.**

I just use Package Manager Console and the below command to regenerate the entity classes whenever I make a change to the database. However, there are a few things it doesn't do quite right and will cause this not to compile. Those are listed below.

# Example

`Scaffold-DbContext 'server=<server>;port=3306;database=Crypto;uid=<user>;password=<password>' Pomelo.EntityFrameworkCore.MySql -ContextDir Contexts -OutputDir Domains -Context CryptoContext -force`

# Required Fixes

**CryptoContext**
* Remove default constructor
* Remove OnConfiguring
* Remove default for Balance/Exclude
* Remove default for Balance/BalanceTarget
* Remove default for Balance/BuyTarget
* Remove default for Balance/SellTarget

**Model**
* CryptoCurrency - Exclude should be `bool?`
* Transaction - ExternalId should be `string`
* Balance - Exclude should be `bool`
