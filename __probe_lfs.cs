using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

var client = new HttpClient { BaseAddress = new Uri("http://192.168.2.178/") };
using var content = new MultipartFormDataContent();
content.Add(new StringContent("/temp/probe.jpg", Encoding.UTF8), "path");
var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
var file = new ByteArrayContent(bytes);
file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
content.Add(file, "file", "probe.jpg");
var response = await client.PostAsync("littlefs_put", content);
Console.WriteLine((int)response.StatusCode);
Console.WriteLine(await response.Content.ReadAsStringAsync());
