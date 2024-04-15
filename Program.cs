using System.Collections.ObjectModel;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using RestSharp;
using SeleniumExtras.WaitHelpers;

namespace UCOBankScrapper
{
    internal abstract class Program
    {
        private static string? UserId;
        private static string? Password;
        private static string? Mobile;
        private static string? Upiid;

        private const string BankUrl = "https://feba.bobibanking.com/corp/AuthenticationController?FORMSGROUP_ID__=AuthenticationFG&__START_TRAN_FLAG__=Y&FG_BUTTONS__=LOAD&ACTION.LOAD=Y&AuthenticationFG.LOGIN_FLAG=1&BANK_ID=012&language=English";
        private static readonly string UpiIdStatusURL = "https://91.playludo.app/api/CommonAPI/GetUpiStatus?upiId=" + Upiid;
        private const string SaveTransactionUrl = "https://91.playludo.app/api/CommonAPI/SavebankTransaction";
        private static readonly string UpiIdUpdateUrl = "https://91.playludo.app/api/CommonAPI/UpdateDateBasedOnUpi?upiId=";

        public static void Main(string[] args)
        {
            getConfig();
            startAgain:
            if (GetBankStatusViaUPIId() != "1")
            {
                log("UPI ID is not active");
                sleep(10);
                goto startAgain;
            }

            var driver = InitBrowser();
            try
            {
                while (true)
                {
                    var transactionList = GetTransaction(driver);
                    SaveTransaction(transactionList);
                }
            }
            catch (Exception Ex)
            {
                log(Ex.Message);
                LogOutButton(driver);
                driver.Quit();
                sleep(10);
                goto startAgain;
            }
        }
        
        private static IWebDriver InitBrowser()
        {
            log("Initializing browser...");
            var chromeOptions = new ChromeOptions
            {
                PageLoadStrategy = PageLoadStrategy.Eager
            };

            string currentDirectory = Directory.GetCurrentDirectory();
            log("Current directory: " + currentDirectory);

            chromeOptions.AddArgument("--disable-blink-features");
            chromeOptions.AddArgument("--disable-blink-features=AutomationControlled");
            chromeOptions.AddArgument("--log-level=3");

            IWebDriver driver = new ChromeDriver(chromeOptions);
            log("Browser initialized.");
            driver.Manage().Window.Maximize();
            try
            {
                string ReturnStatus = Login(driver); // 1 for success, 2 for failure
                if (ReturnStatus == "1")
                {
                    log("Login Successful");
                    return driver;
                }

                log("Failed to login...");
                log("Closing browser...");
                driver.Quit();
            }
            catch (Exception Ex)
            {
                log("Failed to login...");
                log(Ex.Message);
                log("Closing browser...");
                driver.Quit();
            }

            return driver;
        }

        private static string Login(IWebDriver driver)
        {
            var solvedCaptcha = "";
            try
            {
                log("Logging in...");
                driver.Navigate().GoToUrl(BankUrl);
                WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
                DateTime currentDateTime = DateTime.Now;
                bool Islogin = true;
                while (Islogin)
                {
                    TypingElement(driver, 10, By.Id("AuthenticationFG.USER_PRINCIPAL"), UserId, "Username");
                    wait.Until(ExpectedConditions.ElementIsVisible(By.XPath("/html/body/form/div/div/div[9]/div[1]/div[1]/div[4]/span/span/i")));
                    driver.FindElement(By.XPath("/html/body/form/div/div/div[9]/div[1]/div[1]/div[4]/span/span/i")).Click();
                    
                    TypingElement(driver, 10, By.Id("AuthenticationFG.MOBILENUMBER"), Mobile, "Mobile Number");
                    wait.Until(ExpectedConditions.ElementIsVisible(By.XPath("/html/body/form/div/div/div[6]/div/p[2]/span[1]/span/i")));
                    driver.FindElement(By.XPath("/html/body/form/div/div/div[6]/div/p[2]/span[1]/span/i")).Click();
                    
                    log("OTP Login");
                    wait.Until(ExpectedConditions.ElementIsVisible(By.Id("AuthenticationFG.SMS_OTP")));
                    string GetOTP = GetOTPFromAPI(currentDateTime);
                    if (GetOTP.Length != 6)
                        return "2";
                
                    log("OTP is " + GetOTP);
                    TypingElement(driver, 10, By.Id("AuthenticationFG.SMS_OTP"), GetOTP, "OTP Input");
                    
                    wait.Until(ExpectedConditions.ElementIsVisible(By.XPath("/html/body/form/div/div/div[6]/div/p[2]/span[1]/span/i")));
                    driver.FindElement(By.XPath("/html/body/form/div/div/div[6]/div/p[2]/span[1]/span/i")).Click();
                    
                    InputPasswordAgain:
                    TypingElement(driver, 10, By.Id("AuthenticationFG.ACCESS_CODE"), Password, "Password");
                    
                    wait.Until(ExpectedConditions.ElementIsVisible(By.XPath("/html/body/form/div/div/div[9]/div[1]/div[1]/p[5]/span[2]/img")));
                    var imageCaptcha = driver.FindElement(By.XPath("/html/body/form/div/div/div[9]/div[1]/div[1]/p[5]/span[2]/img"));
                    
                    var elementScreenshot = ((ITakesScreenshot)imageCaptcha).GetScreenshot();
                    
                    log("Trying for captcha");
                    string newVar = elementScreenshot.AsBase64EncodedString;
                    solvedCaptcha = GetCaptchaCodeFromAZCaptch(newVar);
                    
                    if (solvedCaptcha == "1")
                        return "2";
                    
                    log("Captcha code is " + solvedCaptcha);
                    solvedCaptcha = solvedCaptcha.Length > 5 ? solvedCaptcha.Trim().Replace(" ", "") : "123456";
                    
                    TypingElement(driver, 10, By.Id("AuthenticationFG.VERIFICATION_CODE"), solvedCaptcha, "Captcha Input");
                    wait.Until(ExpectedConditions.ElementIsVisible(By.Id("VALIDATE_CREDENTIALS1")));
                    driver.FindElement(By.Id("VALIDATE_CREDENTIALS1")).Click();

                    sleep(2);
                    bool isReloved = CheckCaptchResolved(driver);
                    if (!isReloved)
                    {
                        Islogin = false;
                    }
                    else
                    {
                        log("Captcha not resolved. Retrying...");
                        goto InputPasswordAgain;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return "2";
            }

            return "1";
        }
        
        private static object GetTransaction(IWebDriver driver)
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(40));
            sleep(2);
            
            List<object> list = new List<object>();
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (GetBankStatusViaUPIId() != "1")
            {
                log("Bank is not active");
                sleep(3);
                return list;
            }

            sleep(2);
            log("Clicked Menu Explorer...");
            wait.Until(ExpectedConditions.ElementIsVisible(By.XPath("/html/body/form/div[1]/div/div[5]/div/div[5]/div[3]/div/div[3]/div[3]/div/div[2]/div[1]/div/div[1]/div/div/div/table/tbody/tr[1]/td[2]/div/a")));
            driver.FindElement(By.XPath("/html/body/form/div[1]/div/div[5]/div/div[5]/div[3]/div/div[3]/div[3]/div/div[2]/div[1]/div/div[1]/div/div/div/table/tbody/tr[1]/td[2]/div/a")).Click();

            log("Clicked on View Mini Transactions...");
            wait.Until(ExpectedConditions.ElementIsVisible(
                By.XPath("/html/body/form/div[1]/div/div[5]/div/div[5]/div[3]/div/div[3]/div[3]/div/div[2]/div[1]/div/div[1]/div/div/div/table/tbody/tr[1]/td[2]/div/div/ul/li/ul/li")));
            driver.FindElement(
                By.XPath("/html/body/form/div[1]/div/div[5]/div/div[5]/div[3]/div/div[3]/div[3]/div/div[2]/div[1]/div/div[1]/div/div/div/table/tbody/tr[1]/td[2]/div/div/ul/li/ul/li")).Click();

            wait.Until(ExpectedConditions.ElementIsVisible(By.XPath("/html/body/form/div/div/div[5]/div/div[5]/div[3]/div[1]/div[2]/div/div/div[1]/div/div[3]/div[3]/div/div/div[1]/div/table/tbody[1]")));
            log("Row visible...");
            
            IWebElement tbl1 = driver.FindElement(By.XPath("/html/body/form/div/div/div[5]/div/div[5]/div[3]/div[1]/div[2]/div/div/div[1]/div/div[3]/div[3]/div/div/div[1]/div/table"));
            ReadOnlyCollection<IWebElement> tableRow = tbl1.FindElements(By.TagName("tbody"));
            log("Row found...");

            int i = tableRow.Count;
            for (int j = 0; j < i; j++)
            {
                var row = tableRow[j];
                var cols = row.FindElements(By.TagName("td"));
                if (cols.Count <= 0) continue;
                string CreatedDate = cols[0].Text;
                string Description = cols[4].Text;
                string Amount = cols[2].Text.Replace(" ", "");
                string AvlBal = cols[3].Text.Replace(" ", "");
                Description = GetTheUTRWithoutUTR(Description);
                
                Amount = Amount.Replace(",", "");
                AvlBal = AvlBal.Replace(",", "");
                string RefNumber = Description;
                string AccountBalance = AvlBal;
                string UPIId = GetUPIId(Description);

                var values = new
                {
                    Description,
                    CreatedDate,
                    Amount,
                    RefNumber,
                    AccountBalance,
                    BankName = "IOB - " + UserId,
                    UPIId,
                    BankLoginId = UserId
                };
                list.Add(values);
            }

            log("Going back to transaction list...");
            driver.FindElement(By.XPath("/html/body/form/div/div/div[5]/div/div[5]/div[3]/div[1]/div[2]/div/div/div[1]/div/div[3]/div[1]/div/div/p/span/span/i")).Click();
            string json = JsonConvert.SerializeObject(list);
            log(json);
            return list;
        }
        
        private static void SaveTransaction(object TransactionList)
        {
            log("Saving the transaction...");
            string json = JsonConvert.SerializeObject(TransactionList);

            var options = new RestClientOptions(SaveTransactionUrl)
            {
                MaxTimeout = -1,
            };
            var client = new RestClient(options);
            var request = new RestRequest("", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddParameter("application/json", json, ParameterType.RequestBody);
            RestResponse response = client.Execute<RestResponse>(request);
            log(response.Content ?? "");
            UpdateUPIDate();
        }
        
        private static string GetOTPFromAPI(DateTime newDateTime)
        {
            try
            {
                int RetryCount = 0;
                while (RetryCount < 10)
                {
                    sleep(5);
                    log("Trying to get OTP from API.. Attempt " + RetryCount);
                    string OTPAPI = getOTPRequest();
                    if (OTPAPI != "")
                    {
                        var responseData = JsonConvert.DeserializeObject<Dictionary<string, string>>(OTPAPI);
                        log(newDateTime.ToString());
                        if (responseData["ErrorMessage"] != "" && DateTime.Parse(responseData["ErrorMessage"]) > newDateTime)
                        {
                            if (responseData["ErrorCode"] == "1")
                            {
                                log("OTP received from API " + responseData["Result"]);

                                return responseData["Result"];
                            }
                        }
                        log("Failed to get OTP from API");
                    }
                    RetryCount++;
                }
            }
            catch (Exception e)
            {
                log("Failed to get OTP from API");
            }
            return "1";
        }
        
        private static string getOTPRequest()
        {
            try
            {
                string OTPurl = "https://91.playludo.app/api/CommonAPI/GetBankPhoneOTPViaUPIId?UpiId=" + UserId;
                log("Getting OTP...");
                var options = new RestClientOptions(OTPurl)
                {
                    MaxTimeout = -1
                };
                var client = new RestClient(options);
                var request = new RestRequest("", Method.Get);
                request.AddHeader("Content-Type", "application/json");
                RestResponse response = client.Execute<RestResponse>(request);
                log(response.Content);
                return response.Content;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            return "";
        }
        
        private static string GetCaptchaCodeFromAZCaptch(string ImgStr)
        {
            try
            {
                var options = new RestClientOptions("http://azcaptcha.com/in.php")
                {
                    MaxTimeout = -1,
                };
                var client = new RestClient(options);
                var request = new RestRequest("", Method.Post);
                request.AlwaysMultipartFormData = true;
                request.AddParameter("body", ImgStr);
                request.AddParameter("key", "2tjddcrmhqnwpy9km3l4xrgqvb8xyb7v");
                request.AddParameter("method", "base64");
                request.AddParameter("json", "1");
                RestResponse response = client.Execute<RestResponse>(request);
            
                var responseData = JsonConvert.DeserializeObject<Dictionary<string, string>>(response.Content ?? "");
                
                if (responseData != null)
                {
                    sleep(10);
                    var getCaptcha = GetSolvedCaptchString(responseData["request"]);
;                   return getCaptcha;
                }
                else
                {
                    return "1";   
                }
            }
            catch (Exception ex)
            {
                log("exception1 " + ex.Message);
                return "1";
            }
        }

        private static string GetSolvedCaptchString(string requestId)
        {
            try
            {
                var options = new RestClientOptions("http://azcaptcha.com/res.php?key=2tjddcrmhqnwpy9km3l4xrgqvb8xyb7v&id="+ requestId +"&action=get")
                {
                    MaxTimeout = -1,
                };
                var client = new RestClient(options);
                var request = new RestRequest("", Method.Get);
                RestResponse response = client.Execute<RestResponse>(request);
                string responseData = response.Content;
                string[] parts = responseData.Split('|');
                return parts[1];
            }
            catch (Exception ex)
            {
                log("exception1 " + ex.Message);
                return "1";
            }
        }
        
        private static string GetBankStatusViaUPIId()
        {
            try
            {
                var options = new RestClientOptions(UpiIdStatusURL + Upiid)
                {
                    MaxTimeout = -1,
                };
                var client = new RestClient(options);
                var request = new RestRequest("", Method.Get);
                RestResponse response = client.Execute<RestResponse>(request);
                var responseData = JsonConvert.DeserializeObject<Dictionary<string, string>>(response.Content ?? "");
                return responseData != null ? responseData["Result"] : "2";
            }
            catch
            {
                return "2";
            }
        }
   
        private static void TypingElement(IWebDriver driver, int waitingtimeinsecond, By Selection, string? InputMessage, string message)
        {
            WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(waitingtimeinsecond));
            log("Waiting for " + message + " field...");
            wait.Until(ExpectedConditions.ElementIsVisible(Selection));
            log("Typing " + message + "...");

            driver.FindElement(Selection).Clear();
            driver.FindElement(Selection).SendKeys(InputMessage);
        }
        private static void UpdateUPIDate()
        {
            try
            {
                var options = new RestClientOptions(UpiIdUpdateUrl + Upiid)
                {
                    MaxTimeout = -1,
                };
                var client = new RestClient(options);
                var request = new RestRequest("", Method.Get);
                request.AddHeader("Content-Type", "application/json");
                RestResponse response = client.Execute<RestResponse>(request);
                log(response.Content ?? "");
            }
            catch (Exception e)
            {
                log(e.Message);
            }
        }

        private static string GetTheUTRWithoutUTR(string description)
        {
            try
            {
                if (description.Contains("UPI"))
                {
                    var split = description.Split('/');
                    var value = split.FirstOrDefault(x => x.Length == 12);
                    if (value != null)
                    {
                        return value + " " + description;
                    }
                    return description;
                }
            }
            catch
            {
                return description;
            }
            return description;
        }

        private static string GetUPIId(string description)
        {
            try
            {
                if (!description.Contains("@")) return "";
                var split = description.Split('/');
                var value = split.FirstOrDefault(x => x.Contains("@"));
                if (value != null)
                {
                    value = value.Replace("From:", "");
                }
                return value;
            }
            catch (Exception ex)
            {
                log(ex.Message);
            }
            return "";
        }
        
        private static bool CheckCaptchResolved(OpenQA.Selenium.IWebDriver driver)
        {
            try
            {
                string currenturl = driver.Url;
                if (currenturl.Contains("Incorrect"))
                {
                    log("Captcha Error");
                    return true;
                }

                log("Captcha solved");
                return false;
            }
            catch (Exception e)
            {
                return true;
            }
        }
        
        private static void getConfig()
        {
            var configurationBuilder = new ConfigurationBuilder();

            configurationBuilder.AddJsonFile("appsettings.json");
            var configuration = configurationBuilder.Build();
            var appSettingsSection = configuration.GetSection("AppSettings");
            UserId = appSettingsSection["UserName"];
            Password = appSettingsSection["Password"];
            Mobile = appSettingsSection["Mobile"];
            Upiid = appSettingsSection["Upiid"];
        }

        private static void LogOutButton(IWebDriver driver)
        {
            try
            {
                WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
                wait.Until(ExpectedConditions.ElementIsVisible(By.XPath("/html/body/form/div[1]/div/div[1]/div/div/div/div[3]/ul/li[4]/p/span/span/a")));
                driver.FindElement(By.XPath("/html/body/form/div[1]/div/div[1]/div/div/div/div[3]/ul/li[4]/p/span/span/a")).Click();
                wait.Until(ExpectedConditions.ElementIsVisible(By.XPath("/html/body/div[11]/div[2]/div/div[3]/div/div/div/p[4]/span/span[2]/a")));
                driver.FindElement(By.XPath("/html/body/div[11]/div[2]/div/div[3]/div/div/div/p[4]/span/span[2]/a")).Click();
                sleep(2);
                driver.Quit();
                log("Logged Out.");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        
        private static void log(string Message)
        {
            string PrintMessage = "[" + DateTime.Now.ToString("MMMM dd, yyyy h:mm:ss tt") + " - " + Upiid + "] " + Message;
            Console.WriteLine(PrintMessage);
        }
        private static void sleep(int Second)
        {
            log("Sleeping for " + Second.ToString() + " Seconds....");
            Thread.Sleep(Second * 1000);
        }
        public class ResponseObject
        {
            [JsonProperty("status")]
            public int Status { get; set; }

            [JsonProperty("request")]
            public string RequestId { get; set; }
        }
    }
}