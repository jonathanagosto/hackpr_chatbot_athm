using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;
using Newtonsoft.Json;

namespace AthMovilBot
{
    [BotAuthentication]
    public class MessagesController : ApiController
    {
        /// <summary>
        /// POST: api/Messages
        /// Receive a message from a user and reply to it
        /// </summary>
        public async Task<HttpResponseMessage> Post([FromBody]Activity activity)
        {
            if (activity != null && activity.GetActivityType() == ActivityTypes.Message)
            {
                ConnectorClient connector = new ConnectorClient(new Uri(activity.ServiceUrl));
                // calculate something for us to return
                //int length = (activity.Text ?? string.Empty).Length;

                //// return our reply to the user
                //Activity reply = activity.CreateReply($"You sent {activity.Text} which was {length} characters");
                //await connector.Conversations.ReplyToActivityAsync(reply);
                try
                {
                    var sessionRequest = new ApiSessionRequest
                    {
                        Username = "rw-test1",
                        Password = "123qwe"
                    };

                    var result = await sessionRequest.Execute();
                    if (result.ResponseStatus.ToLower().Equals("success"))
                    {
                        await Conversation.SendAsync(activity, () => new PaymentDialog(result.Token));
                    }
                    else
                    {
                        activity.CreateReply("We're sorry! There have been a problem with your credentials :-(");
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            else
            {
                HandleSystemMessage(activity);
            }
            var response = Request.CreateResponse(HttpStatusCode.OK);
            return response;
        }

        private Activity HandleSystemMessage(Activity message)
        {
            if (message.Type == ActivityTypes.DeleteUserData)
            {
                // Implement user deletion here
                // If we handle user deletion, return a real message
            }
            else if (message.Type == ActivityTypes.ConversationUpdate)
            {
                // Handle conversation state changes, like members being added and removed
                // Use Activity.MembersAdded and Activity.MembersRemoved and Activity.Action for info
                // Not available in all channels
            }
            else if (message.Type == ActivityTypes.ContactRelationUpdate)
            {
                // Handle add/remove from contact lists
                // Activity.From + Activity.Action represent what happened
            }
            else if (message.Type == ActivityTypes.Typing)
            {
                // Handle knowing tha the user is typing
            }
            else if (message.Type == ActivityTypes.Ping)
            {
            }

            return null;
        }

        [Serializable]
        public class PaymentDialog : IDialog<object>
        {
            protected string _token = string.Empty;
            protected string _phoneNumber = string.Empty;
            protected decimal _amount = 0;
            protected string _referenceNumber = string.Empty;
            protected Guid _parsedReferenceNumber;

            public PaymentDialog(string token)
            {
                _token = token;
            }
            public async Task StartAsync(IDialogContext context)
            {
                await Task.Factory.StartNew(() => context.Wait(MessageReceivedAsync));
            }

            public virtual async Task MessageReceivedAsync(IDialogContext context, IAwaitable<IMessageActivity> argument)
            {
                var message = await argument;

                if (message.Text.ToLower().Contains("request"))
                {
                    await context.PostAsync("What is the phone number?");
                }
                else if (message.Text.ToLower().Contains("verify"))
                {
                    await context.PostAsync("What is the reference number?");
                }
                else if (Regex.Match(message.Text, @"^[2-9]\d{2}-\d{3}-\d{4}$").Success)
                {
                    _phoneNumber = message.Text;
                    await context.PostAsync("What is the payment amount?");
                }
                else if (decimal.TryParse(message.Text, out _amount))
                {
                    PromptDialog.Confirm(
                    context,
                    OnPaymentRequestAsync,
                    string.Format("Request {0:C} to {1}?", _amount, _phoneNumber),
                    "Didn't get that!",
                    promptStyle: PromptStyle.None);
                }
                else if (Guid.TryParse(message.Text, out _parsedReferenceNumber))
                {
                    _referenceNumber = message.Text;
                    PromptDialog.Confirm(
                    context,
                    OnVerifyPaymentAsync,
                    string.Format("Are you sure?", _amount, _phoneNumber),
                    "Didn't get that!",
                    promptStyle: PromptStyle.None);
                }
                else
                {
                    await context.PostAsync("Hello! Would you like to request a payment or verify a payment?");
                }
                context.Wait(MessageReceivedAsync);
            }

            public async Task OnPaymentRequestAsync(IDialogContext context, IAwaitable<bool> argument)
            {
                var confirm = true;
                if (confirm)
                {
                    var request = new ApiRequestPaymentRequest
                    {
                        Token = _token,
                        Phone = _phoneNumber,
                        Amount = _amount
                    };
                    var result = await request.Execute();
                    if (result.ResponseStatus.ToLower().Equals("success"))
                    {
                        _referenceNumber = result.ReferenceNumber;
                        await context.PostAsync($"Your payment request has been sent. Please save this reference: {result.ReferenceNumber} to verify the status of the payment.");
                    }
                    else
                    {
                        await context.PostAsync("We're sorry! There have been a problem with the transaction :-(");
                    }
                }
                else
                {
                    await context.PostAsync("Did not reset count.");
                }
                context.Wait(MessageReceivedAsync);
            }

            public async Task OnVerifyPaymentAsync(IDialogContext context, IAwaitable<bool> argument)
            {
                var confirm = true;
                if (confirm)
                {
                    var request = new ApiVerifyPaymentRequest
                    {
                        Token = _token,
                        ReferenceNumber = _referenceNumber
                    };
                    var result = await request.Execute();
                    if (result.ResponseStatus.ToLower().Equals("success"))
                    {
                        await context.PostAsync($"Your transaction {_referenceNumber} for the amount of {result.Amount:C} status is: {result.TransactionStatus}");
                    }
                    else
                    {
                        await context.PostAsync("We're sorry! There have been a problem with the transaction :-(");
                    }
                }
                else
                {
                    await context.PostAsync("Did not reset count.");
                }
                context.Wait(MessageReceivedAsync);
            }

        }

        public static readonly string athmApiUrl = "http://athmapi.westus.cloudapp.azure.com/athm/";

        public class ApiSessionRequest : IApiRequest
        {
            public string Url { get { return "requestSession"; } }

            [JsonProperty("commUsername")]
            public string Username { get; set; }

            [JsonProperty("commPassword")]
            public string Password { get; set; }

            public string QueryParameters()
            {
                return string.Format("?commUsername={0}&commPassword={1}", Username, Password);
            }

            public async Task<ApiSessionResponse> Execute()
            {
                var client = new HttpClient();
                client.BaseAddress = new Uri($"{athmApiUrl}{Url}");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                HttpResponseMessage response = await client.GetAsync(QueryParameters());

                if (response.IsSuccessStatusCode)
                {
                    var resAsString = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<ApiSessionResponse>(resAsString);
                }
                throw new NotImplementedException();
            }
        }

        public class ApiSessionResponse
        {
            [JsonProperty("token")]
            public string Token { get; set; }

            [JsonProperty("expDate")]
            public string ExpirationDate { get; set; }

            [JsonProperty("responseStatus")]
            public string ResponseStatus { get; set; }
        }

        public class ApiRequestPaymentRequest
        {
            public string Url { get { return "requestPayment"; } }
            [JsonProperty("token")]
            public string Token { get; set; }

            [JsonProperty("phone")]
            public string Phone { get; set; }

            [JsonProperty("amount")]
            public decimal Amount { get; set; }

            public string QueryParameters()
            {
                return string.Format("?token={0}&phone={1}&amount={2}", Token, Phone, Amount);
            }

            public async Task<ApiPaymentResponse> Execute()
            {
                var client = new HttpClient();
                client.BaseAddress = new Uri($"{athmApiUrl}{Url}");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                HttpResponseMessage response = await client.GetAsync(QueryParameters());

                if (response.IsSuccessStatusCode)
                {
                    var resAsString = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<ApiPaymentResponse>(resAsString);
                }
                throw new NotImplementedException();
            }
        }

        public class ApiVerifyPaymentRequest
        {
            public static string Url { get { return "verifyPaymentStatus"; } }
            [JsonProperty("token")]
            public string Token { get; set; }

            [JsonProperty("referenceNumber")]
            public string ReferenceNumber { get; set; }

            public string QueryParameters()
            {
                return string.Format("?token={0}&referenceNumber={1}", Token, ReferenceNumber);
            }

            public async Task<ApiPaymentResponse> Execute()
            {
                var client = new HttpClient();
                client.BaseAddress = new Uri($"{athmApiUrl}{Url}");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                HttpResponseMessage response = await client.GetAsync(QueryParameters());

                if (response.IsSuccessStatusCode)
                {
                    var resAsString = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<ApiPaymentResponse>(resAsString);
                }
                throw new NotImplementedException();
            }
        }

        public class ApiPaymentResponse
        {
            [JsonProperty("referenceNumber")]
            public string ReferenceNumber { get; set; }

            [JsonProperty("phone")]
            public string Phone { get; set; }

            [JsonProperty("amount")]
            public decimal Amount { get; set; }

            [JsonProperty("transStatus")]
            public string TransactionStatus { get; set; }

            [JsonProperty("responseStatus")]
            public string ResponseStatus { get; set; }
        }

        public interface IApiRequest
        {
            string Url { get; }

            string QueryParameters();
        }

    }
}