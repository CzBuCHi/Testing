using System;
using System.Collections.Generic;
using System.Linq;
using Centaur.Data;
using EasyMarket.Server.ShopOrders.Interfaces;
using EasyWeb.Server;
using EasyWeb.Server.ProductsList;
using Zeus;

[WebSiteAppCode]
public class ShopOrdersInterfaces : IShopCartAddItem, IShopCartRevalidate, IProductsListProductTrigger
{
    public void ShopCartBeforeAddItem(IShopCartAddItemArgs args)
    {
        List<string> variants = new List<string>();

        if (args.AddItemData.ItemParams.ContainsAndNotNullOrEmpty("size"))
        {
            variants.Add("velikost: " + args.AddItemData.ItemParams["size"]);
        }

        if (args.AddItemData.ItemParams.ContainsAndNotNullOrEmpty("color"))
        {
            variants.Add("barva: " + args.AddItemData.ItemParams["color"]);
        }

        args.AddItemData.Variant = string.Join(", ", variants);
    }

    #region Implementation of IProductsListProductTrigger

    public void ProductChangeTrigger(IProductsListProductTriggerArgs args)
    {
        if(args.ListId == WebSiteConstants.ProductsListTest)
        {
            if (args.ChangeType != ProductsListUserChangeType.Deleted)
            {
                ProductInfo productInfo = args.ProductInfo;
                string avail = productInfo.UserDec1 > 0 ? "skladem" : "neni_skladem";
                if (productInfo.UserData2 != avail)
                {
                    ProductInfo newProductInfo = new ProductInfo();
                    newProductInfo.UserData2 = avail;
                    ProductsListWebSiteInterface.ItemUpdate(args.ProductId, newProductInfo);
                }
            }
        }
        
    }
    #endregion

    public void ShopCartRevalidate(IShopCartRevalidateArgs args)
    {
        //foreach (var item in args.CartData.Items)
        //{
        //    item.UnitWeight
        //}
    }
}