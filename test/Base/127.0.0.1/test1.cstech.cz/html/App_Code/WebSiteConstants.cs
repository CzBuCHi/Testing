using EasyWeb.WebSite;
using System;

public static class WebSiteConstants
{
    public const string ProductsListMain = "dbc354f4-7174-47dd-b76e-c7ec4155b1b0";
    public const string ProductsListNews = "26e133f3-8403-4fcd-aab1-2c804046474b";

    public static readonly Uuid ProductsListTest = "df2aca3a-21e9-4b0f-86c7-0c8ed753572d";

    public static Guid ProductsListMainGuid
    {
        get { return new Guid(ProductsListMain); }
    }

    public static Guid ProductsListNewsGuid
    {
        get { return new Guid(ProductsListNews); }
    }

    public const string ImageFilterKatalogBig = "fb964d61-6793-4785-9e94-7b9bf7e696e5";
    public const string ImageFilterKatalogDetail = "86bf079b-b0ed-4ed2-941c-35fbb352c9e7";
    public const string ImageFilterKatalogIcon = "7404b082-85ea-44c5-8814-7eb63e53d5ff";
    public const string ImageFilterKatalogList = "e14fc483-eb9c-4d0d-a766-25a2592d3e75";

    public const string FailImageKatalogBig = "b175c0bf-05e4-4c7f-a089-9d3fc3b31245";
    public const string FailImageKatalogDetail = "b175c0bf-05e4-4c7f-a089-9d3fc3b31245";
    public const string FailImageKatalogIcon = "4f8560d9-66bf-41d8-982d-f764e12d5aaf";
    public const string FailImageKatalogList = "c3201adc-4944-4a22-ab84-02340c4fc1e6";



    public const string SubDataVarianty = "4b38c96f-392f-491f-a5d1-f950a7bb2c0b";

    public static Guid SubDataVariantyGuid {
        get { return new Guid(SubDataVarianty); }
    }
}