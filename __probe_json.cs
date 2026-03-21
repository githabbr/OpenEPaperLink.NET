using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

var client = new HttpClient { BaseAddress = new Uri("http://192.168.2.178/") };
using var content = new MultipartFormDataContent();
content.Add(new StringContent("00000335B9AD3E1C", Encoding.UTF8), "mac");
content.Add(new StringContent("[]", Encoding.UTF8), "json");
var response = await client.PostAsync("jsonupload", content);
Console.WriteLine((int)response.StatusCode);
Console.WriteLine(await response.Content.ReadAsStringAsync());
