using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using EasyWeb.Server.ProductsList.Data;
using EasyWeb.Server.ProductsList.DataAccess;

namespace WebSiteAppCode
{
    /// <summary>
    /// ProductsColors static class contains dictionary ProductColorList filled with colors from EW
    /// </summary>
    public static class ProductsColors
    {
        private static readonly Dictionary<string, string> _ProductColorList = new Dictionary<string, string>();

        static ProductsColors() {
            string[] linesCfg = EasyWeb.WebSite.EasyWebSiteUtils.GetGeneratedStringItem(new Guid("70bfb532-d6ab-4ec0-ba13-3eea16235b42")).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in linesCfg) {
                string[] colPair = line.Split('=');
                if (colPair.Length != 2)
                    continue;
                _ProductColorList.Add(colPair[0], colPair[1]);
            }
        }

        public static string FirstColorOrNull(Guid itemId, string size) {
            string[] info = LoadColors(itemId, size).FirstOrDefault();
            return info != null ? info[0] : null;
        }

        public static string GetColor(string name) {
            string value;
            _ProductColorList.TryGetValue(name, out value);
            return value ?? name;
        }

        public static IEnumerable<string[]> LoadColors(Guid itemId, string size) {
            DataModelProductsListSubDataListProvider providerSubData = new DataModelProductsListSubDataListProvider();
            providerSubData.Request.SubDataTypeId = Guid.Parse("4b38c96f-392f-491f-a5d1-f950a7bb2c0b");
            providerSubData.Request.ProductItemId = itemId;
            IProductsListSubDataListModel subData = providerSubData.SelectDataModel();

            return from subItem in subData.Items
                   where size == null || subItem.UserData1 == size
                   orderby subItem.Id
                   where !string.IsNullOrEmpty(subItem.UserData5)
                   group subItem.UserData3 by subItem.UserData5 into grp
                   select new[] { grp.Key, grp.First() };          
        }
    }
}