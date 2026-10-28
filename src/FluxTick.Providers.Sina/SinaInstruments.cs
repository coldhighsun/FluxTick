using FluxTick.Abstractions;

namespace FluxTick.Providers.Sina;

/// <summary>
/// The currency pairs and commodity futures the app offers for Sina. The symbol search of the service finds neither, so
/// the common ones are listed here; any other code of the service can still be typed in.
/// </summary>
internal static class SinaInstruments
{
    /// <summary>
    /// The instruments: the code of the service, the name the service gives it, and an English name to search by.
    /// </summary>
    private static readonly (string Code, string Name, string EnglishName, AssetClass AssetClass)[] Known =
    [
        ("fx_susdcny", "美元人民币", "USD/CNY onshore yuan", AssetClass.Forex),
        ("fx_susdcnh", "美元离岸人民币", "USD/CNH offshore yuan", AssetClass.Forex),
        ("fx_seurusd", "欧元兑美元", "EUR/USD euro dollar", AssetClass.Forex),
        ("fx_sgbpusd", "英镑兑美元", "GBP/USD pound dollar", AssetClass.Forex),
        ("fx_susdjpy", "美元兑日元", "USD/JPY dollar yen", AssetClass.Forex),
        ("fx_saudusd", "澳元兑美元", "AUD/USD australian dollar", AssetClass.Forex),
        ("fx_snzdusd", "新西兰元兑美元", "NZD/USD new zealand dollar", AssetClass.Forex),
        ("fx_susdcad", "美元兑加元", "USD/CAD canadian dollar", AssetClass.Forex),
        ("fx_susdchf", "美元兑瑞士法郎", "USD/CHF swiss franc", AssetClass.Forex),
        ("fx_susdhkd", "美元兑港元", "USD/HKD hong kong dollar", AssetClass.Forex),
        ("fx_susdsgd", "美元兑新加坡元", "USD/SGD singapore dollar", AssetClass.Forex),
        ("fx_seurcny", "欧元兑人民币", "EUR/CNY euro yuan", AssetClass.Forex),
        ("fx_seurjpy", "欧元兑日元", "EUR/JPY euro yen", AssetClass.Forex),
        ("fx_scnyjpy", "人民币兑日元", "CNY/JPY yuan yen", AssetClass.Forex),
        ("hf_GC", "纽约黄金", "COMEX gold", AssetClass.Commodity),
        ("hf_SI", "纽约白银", "COMEX silver", AssetClass.Commodity),
        ("hf_XAU", "伦敦金（现货黄金）", "London spot gold", AssetClass.Commodity),
        ("hf_XAG", "伦敦银（现货白银）", "London spot silver", AssetClass.Commodity),
        ("hf_CL", "纽约原油", "WTI crude oil", AssetClass.Commodity),
        ("hf_OIL", "布伦特原油", "Brent crude oil", AssetClass.Commodity),
        ("hf_NG", "美国天然气", "natural gas", AssetClass.Commodity),
        ("hf_HO", "美燃油", "heating oil", AssetClass.Commodity),
        ("hf_HG", "美铜", "COMEX copper", AssetClass.Commodity),
        ("hf_CAD", "伦铜", "LME copper", AssetClass.Commodity),
        ("hf_AHD", "伦铝", "LME aluminum aluminium", AssetClass.Commodity),
        ("hf_NID", "伦镍", "LME nickel", AssetClass.Commodity),
        ("hf_PBD", "伦铅", "LME lead", AssetClass.Commodity),
        ("hf_SND", "伦锡", "LME tin", AssetClass.Commodity),
        ("hf_ZSD", "伦锌", "LME zinc", AssetClass.Commodity),
        ("hf_S", "美国大豆", "soybeans", AssetClass.Commodity),
        ("hf_C", "美国玉米", "corn", AssetClass.Commodity),
        ("hf_W", "美国小麦", "wheat", AssetClass.Commodity),
        ("hf_BO", "美黄豆油", "soybean oil", AssetClass.Commodity),
        ("hf_SM", "美黄豆粉", "soybean meal", AssetClass.Commodity),
        ("hf_CT", "美国棉花", "cotton", AssetClass.Commodity),
        ("hf_KC", "美国咖啡", "coffee", AssetClass.Commodity),
    ];

    /// <summary>
    /// Finds the listed instruments that match a query by code, name or English name.
    /// </summary>
    /// <param name="query">
    /// The text to look for, without regard to case.
    /// </param>
    /// <param name="assetClass">
    /// The class to keep, or <see langword="null"/> for every class.
    /// </param>
    /// <param name="providerId">
    /// The id of the provider the assets belong to.
    /// </param>
    /// <returns>
    /// The matching instruments, in the order of the list.
    /// </returns>
    public static IReadOnlyList<Asset> Search(string query, AssetClass? assetClass, string providerId)
    {
        var text = query.Trim();
        if (text.Length == 0)
        {
            return [];
        }

        return
        [
            .. Known
                .Where(i => (assetClass is null || i.AssetClass == assetClass)
                    && (i.Code.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || i.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || i.EnglishName.Contains(text, StringComparison.OrdinalIgnoreCase)))
                .Select(i => new Asset(providerId, i.Code, i.Name, i.AssetClass)),
        ];
    }
}
