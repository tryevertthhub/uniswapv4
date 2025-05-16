using System;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.Web3;
using Nethereum.Util;
using Nethereum.ABI;
using Nethereum.ABI.FunctionEncoding;
using Nethereum.Uniswap.V4.PositionManager;
using Nethereum.Uniswap.V4.StateView;
using Nethereum.Uniswap.V4.StateView.ContractDefinition;
using Nethereum.Uniswap.V4.V4Quoter.ContractDefinition;
using Nethereum.Hex.HexConvertors.Extensions; 
using System.Net.Http;
using System.Text.Json;
using Nethereum.JsonRpc.Client;
using System.Reflection.Metadata.Ecma335;
using Nethereum.ABI.Encoders;
using Nethereum.ABI.Model;
using System.Drawing;
using System;
using System.Linq;
using System.Text;
using Nethereum.Hex.HexTypes;
using Nethereum.StandardTokenEIP20;
using PeterO.Numbers;
using MathNet.Symbolics;
using Expr = MathNet.Symbolics.SymbolicExpression;

using static Program;

class Program
{
    public class TokenInfo
    {
        public int TokenDecimal { get; set; }
        public string TokenSymbol { get; set; }
        public string TokenUsdPrice { get; set; }
    }

    public class PositionInfoV6
    {
        public BigInteger Liquidity { get; set; }
        public BigInteger FeeGrowthInside0LastX128 { get; set; }
        public BigInteger FeeGrowthInside1LastX128 { get; set; }
    }

    public class UncollectedFeesV6
    {
        public BigInteger Fee0 { get; set; }
        public BigInteger Fee1 { get; set; }
    }

    public class TokenAmountsAgs
    {
        public BigInteger Liquidity { get; set; }
        public BigInteger SqrtPriceX96 { get; set; }
        public int TickLow { get; set; }
        public int TickHigh { get; set; }
        public int Token0Decimal { get; set; }
        public int Token1Decimal { get; set; }
    }

    public class TokenAmountsResult
    {
        public EDecimal Token0Amount { get; set; }
        public EDecimal Token1Amount { get; set; }
    }
    static async Task Main(string[] args)
    {

    }
}