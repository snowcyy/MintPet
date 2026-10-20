using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace MintPet
{
    public sealed class BalanceInfo
    {
        public string Currency = "";
        public decimal Total;
        public decimal Granted;
        public decimal ToppedUp;

        public string Symbol
        {
            get
            {
                if (Currency == "CNY") return "¥";
                if (Currency == "USD") return "$";
                return Currency + " ";
            }
        }

        public string Money
        {
            get { return Symbol + Total.ToString("0.00", CultureInfo.InvariantCulture); }
        }
    }

    public sealed class BalanceResult
    {
        public bool Ok;
        public string Error = "";
        public bool Available;
        public BalanceInfo Primary;
        public readonly List<BalanceInfo> All = new List<BalanceInfo>();
    }

    /// <summary>
    /// Client for GET https://api.deepseek.com/user/balance.
    /// The request runs on a worker thread; callers must not touch UI here.
    /// </summary>
    public static class DeepSeekBalance
    {
        public const string DefaultBaseUrl = "https://api.deepseek.com";

        public static BalanceResult Fetch(string apiKey, string baseUrl, int timeoutMs)
        {
            BalanceResult result = new BalanceResult();

            try
            {
                // .NET Framework defaults to old TLS on some machines.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;

                if (string.IsNullOrEmpty(baseUrl)) baseUrl = DefaultBaseUrl;
                string url = baseUrl.TrimEnd('/') + "/user/balance";

                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
                {
                    result.Error = "余额接口地址无效。";
                    return result;
                }

                // Never send the bearer token in clear text. Loopback stays allowed
                // for the offline mock server.
                if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
                {
                    result.Error = "为安全起见，余额接口必须使用 HTTPS（本地调试可用 127.0.0.1）。";
                    return result;
                }

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
                request.Method = "GET";
                request.Accept = "application/json";
                request.UserAgent = "MintPet/1.0";
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                request.KeepAlive = false;
                // Do not follow redirects: the Authorization header must never be
                // forwarded to a host we did not dial ourselves.
                request.AllowAutoRedirect = false;
                request.Headers["Authorization"] = "Bearer " + apiKey;

                try
                {
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        int status = (int)response.StatusCode;
                        if (status >= 300 && status < 400)
                        {
                            result.Error = "接口返回了跳转（HTTP " + status + "），已阻止以避免 API Key 被转发。";
                            return result;
                        }

                        using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                        {
                            string body = reader.ReadToEnd();
                            return ParseBody(body);
                        }
                    }
                }
                catch (WebException web)
                {
                    result.Error = Redact.Text(DescribeWebException(web), apiKey);
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Error = Redact.Text(ex.Message, apiKey);
                return result;
            }
        }

        private static BalanceResult ParseBody(string body)
        {
            object parsed = Json.Parse(body);
            Dictionary<string, object> root = parsed as Dictionary<string, object>;
            if (root == null) throw new FormatException("接口返回的不是 JSON 对象。");

            BalanceResult result = new BalanceResult();

            object available;
            if (root.TryGetValue("is_available", out available) && available is bool)
                result.Available = (bool)available;

            object infosValue;
            List<object> infos = root.TryGetValue("balance_infos", out infosValue) ? infosValue as List<object> : null;
            if (infos == null) throw new FormatException("接口返回里缺少 balance_infos。");

            for (int i = 0; i < infos.Count; i++)
            {
                Dictionary<string, object> row = infos[i] as Dictionary<string, object>;
                if (row == null) continue;

                BalanceInfo info = new BalanceInfo();
                info.Currency = ReadString(row, "currency");
                info.Total = ReadDecimal(row, "total_balance");
                info.Granted = ReadDecimal(row, "granted_balance");
                info.ToppedUp = ReadDecimal(row, "topped_up_balance");
                result.All.Add(info);
            }

            if (result.All.Count == 0) throw new FormatException("接口没有返回余额信息。");

            result.Primary = result.All[0];
            for (int i = 0; i < result.All.Count; i++)
            {
                if (result.All[i].Currency == "CNY")
                {
                    result.Primary = result.All[i];
                    break;
                }
            }

            result.Ok = true;
            return result;
        }

        private static string ReadString(Dictionary<string, object> row, string key)
        {
            object value;
            if (!row.TryGetValue(key, out value) || value == null) return "";
            return value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static decimal ReadDecimal(Dictionary<string, object> row, string key)
        {
            object value;
            if (!row.TryGetValue(key, out value) || value == null) return 0m;

            if (value is double) return (decimal)(double)value;
            if (value is string)
            {
                decimal parsed;
                if (decimal.TryParse((string)value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                    return parsed;
            }
            return 0m;
        }

        private static string DescribeWebException(WebException web)
        {
            HttpWebResponse response = web.Response as HttpWebResponse;
            if (response != null)
            {
                // read everything that is needed before the response is disposed
                int code = (int)response.StatusCode;
                string description = response.StatusDescription;
                string body = null;

                try
                {
                    using (response)
                    using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    {
                        body = reader.ReadToEnd();
                    }
                }
                catch
                {
                }

                if (code >= 300 && code < 400) return "接口返回了跳转（HTTP " + code + "），已阻止以避免 API Key 被转发。";
                if (code == 401) return "HTTP 401：API Key 无效";
                if (code == 402) return "HTTP 402：余额不足";
                if (code == 429) return "HTTP 429：请求太频繁";

                string message = TryReadErrorMessage(body);
                if (!string.IsNullOrEmpty(message)) return message;

                return "HTTP " + code + " " + description;
            }

            switch (web.Status)
            {
                case WebExceptionStatus.Timeout: return "连接超时";
                case WebExceptionStatus.NameResolutionFailure: return "无法解析服务器地址，请检查网络";
                case WebExceptionStatus.ConnectFailure: return "无法连接服务器";
                case WebExceptionStatus.TrustFailure: return "HTTPS 证书校验失败";
                default: return web.Message;
            }
        }

        private static string TryReadErrorMessage(string body)
        {
            try
            {
                Dictionary<string, object> root = Json.Parse(body) as Dictionary<string, object>;
                if (root == null) return null;

                object errorValue;
                if (!root.TryGetValue("error", out errorValue)) return null;

                Dictionary<string, object> error = errorValue as Dictionary<string, object>;
                if (error == null) return null;

                object message;
                if (error.TryGetValue("message", out message)) return message as string;
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
