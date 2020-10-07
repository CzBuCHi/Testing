using System;
using System.Collections.Generic;
using System.Linq;
using EasyMarket.Server.ShopOrders.Data;
using EasyMarket.Server.ShopOrders.Interfaces;
using EasyWeb.Server;
using EasyWeb.WebSite.Ajax;

namespace WebSiteShared
{
    public static class CustomerHelper
    {
        public static void LoginCustomerForm(CustomerLoginFormData customerLoginForm, AjaxActionResult actionResult, bool fromPopup) {
            // can be used for form validation before customer login
            bool loggedId = OrdersWebSiteInterface.Login(customerLoginForm.LoginName, customerLoginForm.Password);
            if (!loggedId) {
                actionResult.AddErrorMessage("Přihlášení se nezdařilo! <br />Vámi zadané <b>jméno</b> nebo <b>heslo</b> není správné. Prosím zkuste to znovu nebo kontaktujte provozovatele obchodu.");
            } else {
                if (fromPopup) {
                    actionResult.AddJavaScriptCode("$.magnificPopup.close();");
                }
            }
        }

        public static void RegisterCustomerForm(CustomerRegistrationFormData customerRegistrationForm, AjaxActionResult actionResult) {
            // can be used for form validation before customer registration
            try {
                OrdersWebSiteInterface.RegisterCustomer(customerRegistrationForm);
                actionResult.AddJavaScriptCode("$.magnificPopup.close();");
            } catch (EasyWebServerException ex) {
                actionResult.AddErrorMessage(ex.Message);
            }
        }
    }
}