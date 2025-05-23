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
        public decimal TokenUsdPrice { get; set; }
    }

    public class PositionInfoV6
    {
        public BigInteger Liquidity { get; set; }
        public BigInteger FeeGrowthInside0LastX128 { get; set; }
        public BigInteger FeeGrowthInside1LastX128 { get; set; }
    }

    public class UncollectedFeesV6
    {
        public BigInteger Fees0 { get; set; }
        public BigInteger Fees1 { get; set; }
    }

    public class TokenAmountsArgs
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

    public static class TickMath
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

    public class TokenInfoService
    {
        private readonly Web3 _web3;
        private const string zeroAddress = "0x0000000000000000000000000000000000000000";

        public TokenInfoService(Web3 web3)
        {
            _web3 = web3;
        }

        public async Task<TokenInfo> GetTokenInfoAsync(string tokenAddress)
        {
            decimal usdPrice = await TokenPriceFetcher.GetTokenUsdPriceAsync(tokenAddress);

            if (tokenAddress.Equals(zeroAddress, StringComparison.OrdinalIgnoreCase))
            {
                return new TokenInfo
                {
                    TokenDecimal = 18,
                    TokenSymbol = "ETH",
                    TokenUsdPrice = usdPrice
                };
            }

            var tokenService = new StandardTokenService(_web3, tokenAddress);
            var tokenDecimal = await tokenService.DecimalsQueryAsync();
            var tokenSymbol = await tokenService.SymbolQueryAsync();

            return new TokenInfo
            {
                TokenDecimal = (int)tokenDecimal,
                TokenSymbol = tokenSymbol,
                TokenUsdPrice = usdPrice
            };
        }
    }

    internal class Address
    {
        public string Value { get; }

        public Address(string address)
        {
            if (!address.StartsWith("0x") || address.Length != 42)
                throw new ArgumentException("Invalid Ethereum address.");

            Value = address;
        }

        public static implicit operator string(Address a) => a.Value;
        public static implicit operator Address(string s) => new Address(s);
    }

    public class UniswapMath
    {
        private static readonly EDecimal Q96 = EDecimal.FromInt32(2).Pow(96);

        public static TokenAmountsResult GetTokenAmounts(TokenAmountsArgs args)
        {
            var ctx = EContext.ForPrecision(100)
            .WithRounding(ERounding.HalfEven)
            .WithExponentClamp(false)
            .WithExponentRange(-1000000, 1000000);

            var liquidity = EDecimal.FromString(args.Liquidity.ToString());
            var sqrtPriceX96 = EDecimal.FromString(args.SqrtPriceX96.ToString());

            // Use MathNet.Symbolics to compute sqrt ratios
            var sqrtRatioA = GetSqrtRatio(args.TickLow);
            var sqrtRatioB = GetSqrtRatio(args.TickHigh);

            var sqrtPrice = sqrtPriceX96.Divide(Q96, ctx);
            var currentTick = GetTickAtSqrtRatio(sqrtPriceX96);

            EDecimal amount0wei = EDecimal.Zero;
            EDecimal amount1wei = EDecimal.Zero;

            if (currentTick < args.TickLow)
            {
                amount0wei = liquidity
                    .Multiply(sqrtRatioB.Subtract(sqrtRatioA))
                    .Divide(sqrtRatioA.Multiply(sqrtRatioB), ctx)
                    .RoundToExponent(0, ERounding.Floor);
            }
            else if (currentTick >= args.TickHigh)
            {
                amount1wei = liquidity
                    .Multiply(sqrtRatioB.Subtract(sqrtRatioA))
                    .RoundToExponent(0, ERounding.Floor);
            }
            else
            {
                var numerator0 = sqrtRatioB.Subtract(sqrtPrice);
                var denominator0 = sqrtPrice.Multiply(sqrtRatioB);
                amount0wei = liquidity.Multiply(numerator0).Divide(denominator0, ctx).RoundToExponent(0, ERounding.Floor);
                amount1wei = liquidity.Multiply(sqrtPrice.Subtract(sqrtRatioA)).RoundToExponent(0, ERounding.Floor);
            }

            var token0Divisor = EDecimal.FromInt32(10).Pow(args.Token0Decimal);
            var token1Divisor = EDecimal.FromInt32(10).Pow(args.Token1Decimal);

            return new TokenAmountsResult
            {
                Token0Amount = amount0wei.Divide(token0Divisor, ctx).RoundToExponent(-6, ERounding.HalfEven),
                Token1Amount = amount1wei.Divide(token1Divisor, ctx).RoundToExponent(-6, ERounding.HalfEven),
            };
        }

        public static int GetTickAtSqrtRatio(EDecimal sqrtPriceX96)
        {
            EContext safeContext = EContext.ForPrecision(100)
                                            .WithExponentClamp(false)
                                            .WithExponentRange(-1000000, 1000000);

            var sqrtPrice = sqrtPriceX96.Divide(Q96, EContext.Unlimited);
            var price = sqrtPrice.Multiply(sqrtPrice);

            var logPrice = price.Log(safeContext);
            var logBase = EDecimal.FromString("1.0001").Log(safeContext);

            var tick = logPrice.Divide(logBase, safeContext);
            return tick.RoundToExponent(0, ERounding.Floor).ToInt32Checked();
        }

        private static EDecimal GetSqrtRatio(int tick)
        {
            var exponent = (tick / 2.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var powExpr = Expr.Parse("1.0001").Pow(Expr.Parse(exponent));
            var approx = powExpr.Evaluate(null); // returns FloatingPoint number

            return EDecimal.FromString(approx.RealValue.ToString("G20", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    public class FeeCalculator
    {
        private static readonly BigInteger Q128 = BigInteger.Pow(2, 128);

        public async Task<UncollectedFeesV6> GetUncollectedFeeAsync(
            StateViewService stateView,
            PositionManagerService positionManager,
            byte[] poolId,
            int tickLower,
            int tickUpper,
            int currentTick,
            byte[] salt)
        {
            var poolIdBytes = poolId;

            var feeGrowthGlobalsResult = await stateView.GetFeeGrowthGlobalsQueryAsync(new GetFeeGrowthGlobalsFunction
            {
                PoolId = poolId
            });

            var feeGrowthGlobal0X128 = feeGrowthGlobalsResult.FeeGrowthGlobal0;
            var feeGrowthGlobal1X128 = feeGrowthGlobalsResult.FeeGrowthGlobal1;

            var tickLowerFunction = new GetTickFeeGrowthOutsideFunction
            {
                PoolId = poolId,  // Set the PoolId to the proper Address type
                Tick = tickLower // Set the TickIndex to the tickLower value (of type int or similar)
            };

            var tickUpperFunction = new GetTickFeeGrowthOutsideFunction
            {
                PoolId = poolId,  // Set the PoolId to the proper Address type
                Tick = tickUpper // Set the TickIndex to the tickUpper value
            };
            var tickLowerData = await stateView.GetTickFeeGrowthOutsideQueryAsync(tickLowerFunction);
            var tickUpperData = await stateView.GetTickFeeGrowthOutsideQueryAsync(tickUpperFunction);
            BigInteger feeGrowthOutside0LowerX128 = tickLowerData.FeeGrowthOutside0X128;
            BigInteger feeGrowthOutside1LowerX128 = tickLowerData.FeeGrowthOutside1X128;
            BigInteger feeGrowthOutside0UpperX128 = tickUpperData.FeeGrowthOutside0X128;
            BigInteger feeGrowthOutside1UpperX128 = tickUpperData.FeeGrowthOutside1X128;

            var positionResult = await stateView.GetPositionInfoQueryAsync(
                poolIdBytes,
                positionManager.ContractHandler.ContractAddress,
                tickLower,
                tickUpper,
                salt
            );

            var position = new PositionInfoV6
            {
                Liquidity = positionResult.Liquidity,
                FeeGrowthInside0LastX128 = positionResult.FeeGrowthInside0LastX128,
                FeeGrowthInside1LastX128 = positionResult.FeeGrowthInside1LastX128
            };

            // Fee growth calculations
            BigInteger feeGrowthBelow0X128 = currentTick >= tickLower
                ? feeGrowthOutside0LowerX128
                : feeGrowthGlobal0X128 - feeGrowthOutside0LowerX128;

            BigInteger feeGrowthBelow1X128 = currentTick >= tickLower
                ? feeGrowthOutside1LowerX128
                : feeGrowthGlobal1X128 - feeGrowthOutside1LowerX128;

            BigInteger feeGrowthAbove0X128 = currentTick < tickUpper
                ? feeGrowthOutside0UpperX128
                : feeGrowthGlobal0X128 - feeGrowthOutside0UpperX128;

            BigInteger feeGrowthAbove1X128 = currentTick < tickUpper
                ? feeGrowthOutside1UpperX128
                : feeGrowthGlobal1X128 - feeGrowthOutside1UpperX128;

            var feeGrowthInside0X128 = feeGrowthGlobal0X128 - feeGrowthBelow0X128 - feeGrowthAbove0X128;
            var feeGrowthInside1X128 = feeGrowthGlobal1X128 - feeGrowthBelow1X128 - feeGrowthAbove1X128;

            var uncollectedFees0 = position.Liquidity * (feeGrowthInside0X128 - position.FeeGrowthInside0LastX128) / Q128;
            var uncollectedFees1 = position.Liquidity * (feeGrowthInside1X128 - position.FeeGrowthInside1LastX128) / Q128;

            return new UncollectedFeesV6
            {
                Fees0 = uncollectedFees0,
                Fees1 = uncollectedFees1
            };
        }
    }

    public static PositionInfo DecodePositionInfo(BigInteger packedData)
    {
        var mask200 = (BigInteger.One << 200) - 1;
        var mask24 = (BigInteger.One << 24) - 1;
        var mask8 = (BigInteger.One << 8) - 1;

        var poolId = (packedData >> 56) & mask200;
        var tickUpper = (packedData >> 32) & mask24;
        var tickLower = (packedData >> 8) & mask24;
        var hasSubscriber = (packedData & mask8) != 0;

        return new PositionInfo
        {
            PoolId = "0x" + poolId.ToString("x").PadLeft(50, '0'),
            TickUpper = ToSigned24Bit(tickUpper),
            TickLower = ToSigned24Bit(tickLower),
            HasSubscriber = hasSubscriber
        };
    }

    private static int ToSigned24Bit(BigInteger value)
    {
        var maxSigned = (BigInteger.One << 23) - 1;
        return value > maxSigned ? (int)(value - (BigInteger.One << 24)) : (int)value;
    }

    public static byte[] CreatePoolKey(string currency0, string currency1, uint fee, int tickSpacing, string hooks)
    {
        // Ordina gli address in ordine lessicografico (ignorando maiuscole/minuscole)
        var tokens = new[] { currency0.ToLowerInvariant(), currency1.ToLowerInvariant() };
        Array.Sort(tokens, StringComparer.Ordinal);

        string token0 = tokens[0];
        string token1 = tokens[1];

        // Definizione dei parametri ABI
        var parameters = new[]
         {
          new Parameter("address", 1),
          new Parameter("address", 2),
          new Parameter("uint24", 3),
          new Parameter("int24", 4),
          new Parameter("address", 5)
         };

        // Valori corrispondenti
        var values = new object[]
        {
          token0,
          token1,
          fee,
          tickSpacing,
          hooks
        };

        // Codifica
        var encoder = new Nethereum.ABI.FunctionEncoding.ParametersEncoder();
        var encoded = encoder.EncodeParameters(parameters, values);

        // Hash Keccak256
        var hash = new Sha3Keccack().CalculateHash(encoded);
        return hash;
        //return "0x" + hash;
    }

    public static double TickToPrice(int tick, int token0Decimals, int token1Decimals, bool inverse = false)
    {
        double rawPrice = Math.Pow(1.0001, tick);
        double priceT0inT1 = rawPrice / Math.Pow(10, token1Decimals - token0Decimals);

        return inverse ? (priceT0inT1 == 0 ? double.PositiveInfinity : 1 / priceT0inT1) : priceT0inT1;
    }

    public static PriceRange GetPriceRanges(int tickLower, int tickUpper, int tickCurrent, int token0Decimals, int token1Decimals, bool inverse = false)
    {
        var priceLower = TickToPrice(tickLower, token0Decimals, token1Decimals, inverse);
        var priceUpper = TickToPrice(tickUpper, token0Decimals, token1Decimals, inverse);
        var currentPrice = TickToPrice(tickCurrent, token0Decimals, token1Decimals, inverse);

        return new PriceRange
        {
            MinPrice = Math.Min(priceLower, priceUpper),
            MaxPrice = Math.Max(priceLower, priceUpper),
            CurrentPrice = currentPrice
        };
    }

    public static byte[] HexSalt(int tokenId)
    {
        var hex = tokenId.ToString("x").PadLeft(64, '0');
        return ("0x" + hex).HexToByteArray();
    }

    public static byte[] EncodePoolId(string currency0, string currency1, uint fee, int tickSpacing, string hooks)
    {
        var abiEncode = new ABIEncode();

        var packed = abiEncode.GetABIEncodedPacked(
            new ABIValue("address", currency0),
            new ABIValue("address", currency1),
            new ABIValue("uint24", fee),
            new ABIValue("int24", tickSpacing),
            new ABIValue("address", hooks)
        );

        return new Sha3Keccack().CalculateHash(packed);
    }

    public static decimal FormatUnits(BigInteger value, int decimals)
    {
        return (decimal)value / (decimal)BigInteger.Pow(10, decimals);
    }

    static async Task Main(string[] args)
    {
        var rpcUrl = "https://mainnet.infura.io/v3/10851e6270fa4c30bf54a53b8a083a92";
        var positionId = 2662L;
        var UNISWAPV4_POSITIONMANAGER_ADDR = "0xbd216513d74c8cf14cf4747e6aaa6420ff64ee9e";
        var UNISWAPV4_STATEVIEW_ADDR = "0x7ffe42c4a5deea5b0fec41c94c136cf115597227";
        var web3 = new Web3(rpcUrl);

        var PositionManager = new PositionManagerService(web3, UNISWAPV4_POSITIONMANAGER_ADDR);
        var StateView = new StateViewService(web3, UNISWAPV4_STATEVIEW_ADDR);



        // Get Token Prices (ETH, USDC example)
        // string ethAddress = "0x0000000000000000000000000000000000000000"; // ETH
        // string usdcAddress = "0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48"; // USDC (ERC-20)

        // decimal ethPrice = await TokenPriceFetcher.GetTokenUsdPriceAsync(ethAddress);
        // decimal usdcPrice = await TokenPriceFetcher.GetTokenUsdPriceAsync(usdcAddress);


        // Console.WriteLine($"ETH: ${ethPrice}, USDC: ${usdcPrice}");


        var poolAndHash = await PositionManager.GetPoolAndPositionInfoQueryAsync(positionId);
        var poolKey = (poolAndHash.PoolKey);

        string currency0 = poolKey.Currency0;
        string currency1 = poolKey.Currency1;
        uint fee = poolKey.Fee;
        int tickspacing = poolKey.TickSpacing;
        string hooks = poolKey.Hooks;

        var poolInfo = CreatePoolKey(currency0, currency1, fee, tickspacing, hooks);

        var data = await StateView.GetSlot0QueryAsync(poolInfo);

        var sqrtPriceX96 = data.SqrtPriceX96;
        var currentTick = data.Tick;
        var lpFee = data.LpFee;
        var protocolFee = data.ProtocolFee;

        var positionInfo = DecodePositionInfo(poolAndHash.Info);

        var poolId = EncodePoolId(
            poolKey.Currency0,
            poolKey.Currency1,
            poolKey.Fee,
            poolKey.TickSpacing,
            poolKey.Hooks
        );

        var pool_Info = await StateView.GetPositionInfoQueryAsync(
            poolInfo,
            UNISWAPV4_POSITIONMANAGER_ADDR,
            positionInfo.TickLower,
            positionInfo.TickUpper,
            HexSalt((int)positionId)
        );

        var tokenInfoService = new TokenInfoService(web3);
        // TokenInfo tokenInfo = await tokenInfoService.GetTokenInfoAsync(ethAddress);
        // Console.WriteLine($"Token Symbol: {tokenInfo.TokenSymbol}");
        // Console.WriteLine($"Token Decimals: {tokenInfo.TokenDecimal}");
        // Console.WriteLine($"Token USD Price: ${tokenInfo.TokenUsdPrice}");

        var token0Address = poolKey.Currency0;
        var token1Address = poolKey.Currency1;

        var token0info = await tokenInfoService.GetTokenInfoAsync(token0Address);
        var token0Decimal = token0info.TokenDecimal;
        var token0Symbol = token0info.TokenSymbol;
        var token0USDPrice = token0info.TokenUsdPrice;

        var token1info = await tokenInfoService.GetTokenInfoAsync(token1Address);
        var token1Decimal = token1info.TokenDecimal;
        var token1Symbol = token1info.TokenSymbol;
        var token1USDPrice = token1info.TokenUsdPrice;
        var prices = GetPriceRanges(
              positionInfo.TickLower,
              positionInfo.TickUpper,
              currentTick,
              token0Decimal,
              token1Decimal,
              false
        );

        var liquidity = pool_Info.Liquidity;
        var amountargs = new TokenAmountsArgs
        {
            Liquidity = liquidity,
            SqrtPriceX96 = sqrtPriceX96,
            TickLow = positionInfo.TickLower,
            TickHigh = positionInfo.TickUpper,
            Token0Decimal = token0Decimal,
            Token1Decimal = token1Decimal
        };


        var token_amounts = UniswapMath.GetTokenAmounts(amountargs);

        var ctx = EContext.ForPrecision(100)
                        .WithRounding(ERounding.HalfEven)
                        .WithExponentClamp(false)
                        .WithExponentRange(-1000000, 1000000);

        var token0USD = token_amounts.Token0Amount.Multiply(EDecimal.FromString(token0USDPrice.ToString()));
        var token1USD = token_amounts.Token1Amount.Multiply(EDecimal.FromString(token1USDPrice.ToString()));
        var totalUsd = token0USD.Add(token1USD, ctx);

        FeeCalculator feeCalculator = new FeeCalculator();

        //Call GetUncollectedFeeAsync to get the uncollected fees
        UncollectedFeesV6 uncollectedFees = await feeCalculator.GetUncollectedFeeAsync(StateView, PositionManager, poolInfo, positionInfo.TickLower, positionInfo.TickUpper, currentTick, HexSalt((int)positionId));

        // Use the uncollected fees
        decimal token0Fee = FormatUnits(uncollectedFees.Fees0, token0Decimal);
        decimal token1Fee = FormatUnits(uncollectedFees.Fees1, token1Decimal);


        var token0FeeUSD = token0Fee * token0USDPrice;
        var token1FeeUSD = token1Fee * token1USDPrice;
        var tokenFee = token0FeeUSD + token1FeeUSD;

        string poolFee = $"{(double)lpFee / 1_000_000 * 100:F2}%"; // "0.30%" for 3000

        Console.WriteLine($"Current Marketplce Price:{prices.CurrentPrice}");
        Console.WriteLine($"Minimum Price:{prices.MinPrice}");
        Console.WriteLine($"Maximum Price:{prices.MaxPrice}");
        Console.WriteLine($"Total USD value: {totalUsd} USD");
        Console.WriteLine($"Uncollected Fees 0: {token0Fee} {token0Symbol}");
        Console.WriteLine($"Uncollected Fees 1: {token1Fee} {token1Symbol}");
        Console.WriteLine($"Total Fee value: {tokenFee} USD");
        Console.WriteLine(poolFee);
    }
}