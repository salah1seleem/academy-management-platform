using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Academy.Api.Operations;
public sealed class MobilePushProvider(IConfiguration config,IHttpClientFactory clients,TimeProvider time)
{
    public static bool Configured(IConfiguration c)=>File.Exists(c["Push:APNs:PrivateKeyPath"])&&new[]{"TeamId","KeyId","Topic"}.All(k=>!string.IsNullOrWhiteSpace(c[$"Push:APNs:{k}"]))||File.Exists(c["Push:FCM:CredentialsPath"]);
    private static string B64(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static string Part(object value)=>B64(JsonSerializer.SerializeToUtf8Bytes(value));
    private string Jwt(string path,object header,object payload,bool ec)
    {
        var input=Part(header)+"."+Part(payload);var bytes=Encoding.ASCII.GetBytes(input);var key=File.ReadAllText(path);
        if(ec){using var signer=ECDsa.Create();signer.ImportFromPem(key);return input+"."+B64(signer.SignData(bytes,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation));}
        using var rsa=RSA.Create();rsa.ImportFromPem(key);return input+"."+B64(rsa.SignData(bytes,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));
    }
    public async Task<string> Send(string provider,string token,Guid delivery,Guid notice,Guid academy,CancellationToken ct)
    {
        using var http=clients.CreateClient();http.Timeout=TimeSpan.FromSeconds(12);
        if(provider=="APNs"){
            var path=config["Push:APNs:PrivateKeyPath"];var team=config["Push:APNs:TeamId"];var key=config["Push:APNs:KeyId"];var topic=config["Push:APNs:Topic"];
            if(string.IsNullOrWhiteSpace(path)||string.IsNullOrWhiteSpace(team)||string.IsNullOrWhiteSpace(key)||string.IsNullOrWhiteSpace(topic))return "Unconfigured";
            var jwt=Jwt(path,new{alg="ES256",kid=key},new{iss=team,iat=time.GetUtcNow().ToUnixTimeSeconds()},true);
            var host=config.GetValue("Push:APNs:Sandbox",false)?"api.sandbox.push.apple.com":"api.push.apple.com";
            using var request=new HttpRequestMessage(HttpMethod.Post,$"https://{host}/3/device/{token}"){Version=HttpVersion.Version20,VersionPolicy=HttpVersionPolicy.RequestVersionExact,Content=JsonContent.Create(new{aps=new{alert=new{title="iAcademy One",body="لديك تنبيه جديد داخل التطبيق."}},noticeId=notice.ToString(),academyId=academy.ToString()})};
            request.Headers.Authorization=new("bearer",jwt);request.Headers.Add("apns-topic",topic);request.Headers.Add("apns-push-type","alert");request.Headers.Add("apns-id",delivery.ToString());request.Headers.Add("apns-collapse-id",notice.ToString());request.Headers.Add("apns-expiration",time.GetUtcNow().AddHours(6).ToUnixTimeSeconds().ToString());
            using var response=await http.SendAsync(request,ct);return response.IsSuccessStatusCode?"Accepted":response.StatusCode is HttpStatusCode.Gone or HttpStatusCode.BadRequest?"Rejected":"Retry";
        }
        var credentialPath=config["Push:FCM:CredentialsPath"];if(string.IsNullOrWhiteSpace(credentialPath))return "Unconfigured";
        using var credentials=JsonDocument.Parse(await File.ReadAllTextAsync(credentialPath,ct));var root=credentials.RootElement;
        var project=root.GetProperty("project_id").GetString()!;if(!System.Text.RegularExpressions.Regex.IsMatch(project,"\\A[a-z0-9-]{4,64}\\z"))return "Rejected";
        var issued=time.GetUtcNow().ToUnixTimeSeconds();var input=Part(new{alg="RS256",typ="JWT"})+"."+Part(new{iss=root.GetProperty("client_email").GetString(),scope="https://www.googleapis.com/auth/firebase.messaging",aud="https://oauth2.googleapis.com/token",iat=issued,exp=issued+3300});
        using var rsa=RSA.Create();rsa.ImportFromPem(root.GetProperty("private_key").GetString());var assertion=input+"."+B64(rsa.SignData(Encoding.ASCII.GetBytes(input),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));
        using var auth=await http.PostAsync("https://oauth2.googleapis.com/token",new FormUrlEncodedContent(new Dictionary<string,string>{{"grant_type","urn:ietf:params:oauth:grant-type:jwt-bearer"},{"assertion",assertion}}),ct);
        if(!auth.IsSuccessStatusCode)return "Retry";using var access=JsonDocument.Parse(await auth.Content.ReadAsStringAsync(ct));
        using var message=new HttpRequestMessage(HttpMethod.Post,$"https://fcm.googleapis.com/v1/projects/{project}/messages:send"){Content=JsonContent.Create(new{message=new{token,notification=new{title="iAcademy One",body="لديك تنبيه جديد داخل التطبيق."},data=new{noticeId=notice.ToString(),academyId=academy.ToString()},android=new{collapse_key=notice.ToString(),notification=new{tag=notice.ToString()}}}})};
        message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access.RootElement.GetProperty("access_token").GetString());using var sent=await http.SendAsync(message,ct);return sent.IsSuccessStatusCode?"Accepted":sent.StatusCode==HttpStatusCode.NotFound?"Rejected":"Retry";
    }
}
