﻿using System;
using System.Collections.Generic;

#nullable disable

namespace CryptoWatch.Entities.Domains
{
    public partial class Balance
    {
        public int Id { get; set; }
        public string Symbol { get; set; }
        public string AltSymbol { get; set; }
        public string Name { get; set; }
        public bool Exclude { get; set; }
        public decimal? Amount { get; set; }
    }
}