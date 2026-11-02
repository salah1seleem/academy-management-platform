using Academy.Api.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

var connection = Environment.GetEnvironmentVariable("DEPLOYMENT_TEST_DB") ?? throw new Exception("Disposable PostgreSQL connection required");
var cs = new NpgsqlConnectionStringBuilder(connection);
if(!cs.Database!.StartsWith("academy_app_platform_test_") || cs.Host is not ("localhost" or "127.0.0.1"))throw new Exception("Only isolated local test databases allowed");
using var db = new NpgsqlConnection(connection); db.Open();
using(var cmd = new NpgsqlCommand("CREATE TABLE IF NOT EXISTS app_data_protection_keys (id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, xml text NOT NULL)",db))cmd.ExecuteNonQuery();
using var rsa = RSA.Create(2048);
var request = new CertificateRequest("CN=Disposable deployment test",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
var phase=Environment.GetEnvironmentVariable("DEPLOYMENT_TEST_PHASE");
var statePath=Environment.GetEnvironmentVariable("DEPLOYMENT_TEST_STATE") ?? Path.Combine(Path.GetTempPath(),"iacademy-test-state.json");
var saved=phase=="read"?JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(statePath)):null;
using var cert = saved==null ? request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1))
    : X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(saved["certificate"]),null,X509KeyStorageFlags.EphemeralKeySet);
ServiceProvider Provider() {
    var services = new ServiceCollection();services.AddLogging();
    services.AddDataProtection().SetApplicationName("iAcademyOne").ProtectKeysWithCertificate(cert);
    services.Configure<KeyManagementOptions>(o=>o.XmlRepository=new PostgresKeyRepository(connection));
    return services.BuildServiceProvider();
}
TicketDataFormat Cookie(ServiceProvider provider) => new(provider.GetRequiredService<IDataProtectionProvider>()
    .CreateProtector("Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware", "Identity.Application", "v2"));
var identity = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,"synthetic-owner")},"Identity.Application")),"Identity.Application");
string ticket;
if(saved!=null)ticket=saved["ticket"];
else using(var first=Provider())ticket=Cookie(first).Protect(identity);
using(var second=Provider())if(Cookie(second).Unprotect(ticket)?.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!="synthetic-owner")throw new Exception("Session lost");
using(var cmd=new NpgsqlCommand("SELECT xml FROM app_data_protection_keys",db))if(!((string)cmd.ExecuteScalar()!).Contains("encryptedSecret"))throw new Exception("Keys not encrypted");
Console.WriteLine("PASS: encrypted PostgreSQL key ring and session survive independent service providers");
var endpoint=Environment.GetEnvironmentVariable("DEPLOYMENT_TEST_S3") ?? throw new Exception("Local S3 test endpoint required");
if(!new Uri(endpoint).IsLoopback)throw new Exception("Local S3 test endpoint only");
var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
 ["Media:Provider"]="Spaces",["Spaces:Bucket"]="iacademy-test",["Spaces:Endpoint"]=endpoint,
 ["Spaces:Region"]="us-east-1",["Spaces:AccessKey"]="isolated-test",["Spaces:SecretKey"]="isolated-test-secret"
}).Build();
var key=saved!=null?saved["key"]:$"{Guid.NewGuid():N}/{Guid.NewGuid():N}";
if(saved==null)using(var first=new MediaObjectStore(config,new TestEnvironment()))await first.Put(key,new MemoryStream([1,2,3,4]));
using(var anonymous=new HttpClient()) {
    using var result=await anonymous.GetAsync(endpoint.TrimEnd('/')+"/iacademy-test/"+key);
    if(result.IsSuccessStatusCode)throw new Exception("Private object readable anonymously");
}
if(phase=="write") {
    File.WriteAllText(statePath,JsonSerializer.Serialize(new Dictionary<string,string>{{"certificate",Convert.ToBase64String(cert.Export(X509ContentType.Pfx))},{"ticket",ticket},{"key",key}}));
    Console.WriteLine("PASS: first disposable container persisted synthetic object and session state"); return;
}
using(var second=new MediaObjectStore(config,new TestEnvironment())) {
 await using(var bytes=await second.Open(key)){if(bytes==null||!bytes.CanSeek||bytes.Length!=4)throw new Exception("Object lost");bytes.Seek(2,SeekOrigin.Begin);if(bytes.ReadByte()!=3)throw new Exception("Range source invalid");}
 if(await second.Open($"{Guid.NewGuid():N}/{key.Split('/')[1]}")!=null)throw new Exception("Cross academy key collision");
 await second.Delete(key);if(await second.Open(key)!=null)throw new Exception("Deletion failed");
}
Console.WriteLine("PASS: S3 upload, replacement store, seek, tenant namespace and deletion");
sealed class TestEnvironment : IHostEnvironment {
 public string EnvironmentName {get;set;}="Testing"; public string ApplicationName {get;set;}="DeploymentChecks";
 public string ContentRootPath {get;set;}=Path.GetTempPath(); public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();
}
