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

    public class UniswapV4PoolData
    {
        public BigInteger Liquidity { get; set; }
        public decimal MarketPrice { get; set; }
        public int TickLower { get; set; }
        public int TickUpper { get; set; }
    }

    public class PoolKey
    {
        public string Currency0 { get; set; }
        public string Currency1 { get; set; }
        public int Fee { get; set; }
        public int TickSpacing { get; set; }
        public string Hooks { get; set; }
    }

    public class PositionInfo
    {
        public string PoolId { get; set; }
        public int TickUpper { get; set; }
        public int TickLower { get; set; }
        public bool HasSubscriber { get; set; }
    }

    public class PriceRange
    {
        public double MinPrice { get; set; }
        public double MaxPrice { get; set; }
        public double CurrentPrice { get; set; }
    }

    public class TickMath
    {
        public static readonly BigInteger Q96 = BigInteger.Pow(2, 96);
        public static readonly decimal Q96Decimal = (decimal)Math.Pow(2, 96);
        public static readonly double Q96Number = Math.Pow(2, 96);
        public static readonly BigInteger Q128 = BigInteger.Pow(2, 128);
        public static readonly BigInteger MAX_UINT256 = BigInteger.Pow(2, 256) - 1;

        public static BigInteger GetSqrtRatioAtTick(int tick)
        {
            BigInteger absTick = new BigInteger(Math.Abs(tick));
            BigInteger ratio = (absTick & 1) != 0
                ? BigInteger.Parse("3402992956809132418596140100660247210") // 0xfffcb933bd6fad37aa2d162d1a594001
                : BigInteger.Parse("115792089237316195423570985008687907853269984665640564039457584007913129639936"); // 0x100000000000000000000000000000000

            BigInteger[] multipliers = new BigInteger[]
            {
                BigInteger.Parse("340282366920938463463374607431768211456"), // unused index 0 (for offset safety)
                BigInteger.Parse("340264224289222143251188864610255353850"),
                BigInteger.Parse("339977317827889760843129100002153036236"),
                BigInteger.Parse("338512371204189215932299921723028330576"),
                BigInteger.Parse("335662504262999974824578629833512745792"),
                BigInteger.Parse("330995372871051239122285380579718059617"),
                BigInteger.Parse("324460501151540017093199453721619950803"),
                BigInteger.Parse("316268273176316630336589464420168295588"),
                BigInteger.Parse("306636746385157692595153524123857758548"),
                BigInteger.Parse("294934108368675405467148282140268166003"),
                BigInteger.Parse("281584408378933755672323394364861269465"),
                BigInteger.Parse("267083699258620034173180206468138769445"),
                BigInteger.Parse("251780580339280951387927429172287061861"),
                BigInteger.Parse("235845494033721682170730071386171129975"),
                BigInteger.Parse("219180460113689949248524391403450938342"),
                BigInteger.Parse("202188416343993706663792792841168372646"),
                BigInteger.Parse("185117506124987513190582715057299210505"),
                BigInteger.Parse("168135777394056716523257573429324674564"),
                BigInteger.Parse("151561556154234097190094563278016353246"),
                BigInteger.Parse("135942260144472321105408788925591429"), // 0x48a170391f7dc42444e8fa2
            };

            for (int i = 0; i < 19; i++)
            {
                if ((absTick & (BigInteger.One << i)) != 0)
                {
                    ratio = (ratio * multipliers[i + 1]) >> 128;
                }
            }

            if (tick < 0)
            {
                ratio = MAX_UINT256 / ratio;
            }

            // Convert to Q64.96 and round up
            BigInteger sqrtPriceX96 = (ratio >> 32) + ((ratio % (BigInteger.One << 32)) != 0 ? 1 : 0);
            return sqrtPriceX96;
        }
    }

    public class TokenPriceFetcher
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private const string PlatformId = "ethereum";
        private static readonly string ZeroAddress = "0x0000000000000000000000000000000000000000";

        public static async Task<decimal> GetTokenUsdPriceAsync(string tokenAddress)
        {
            string contractAddress = tokenAddress.ToLower();
            string apiId = string.Empty;
            string url = string.Empty;

            if (contractAddress == ZeroAddress.ToLower())
            {
                // Native ETH
                apiId = "ethereum";
                url = $"https://api.coingecko.com/api/v3/simple/price?ids={apiId}&vs_currencies=usd";
            }
            else
            {
                // ERC-20 Token
                apiId = contractAddress;
                url = $"https://api.coingecko.com/api/v3/simple/token_price/{PlatformId}?contract_addresses={apiId}&vs_currencies=usd";
            }

            try
            {
                HttpResponseMessage response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                string content = await response.Content.ReadAsStringAsync();

                using var jsonDoc = JsonDocument.Parse(content);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty(apiId, out JsonElement priceData) &&
                    priceData.TryGetProperty("usd", out JsonElement usdValue))
                {
                    return usdValue.GetDecimal();
                }

                return 0; // Price not found
            }
            catch
            {
                return 0; // Return 0 on error
            }
        }
    }
    
    static async Task Main(string[] args)
    {

    }
}