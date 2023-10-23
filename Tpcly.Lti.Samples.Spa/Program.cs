using IdentityModel.Client;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Tpcly.Lti;
using Tpcly.Lti.Samples.Spa;
using Tpcly.Lti.Samples.Spa.Data;
using Tpcly.Lti.Samples.Spa.Services;
using Tpcly.Persistence.Memory;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Add services to the container.

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDistributedMemoryCache();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = config.GetConnectionString("Default");

    options.UseMySql(
        connectionString,
        ServerVersion.AutoDetect(connectionString)
    );
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => { policy.WithOrigins(config["Lti:TargetUri"]).AllowAnyMethod().AllowAnyHeader().AllowCredentials(); });
});

builder.Services.AddLti()
    .AddToolProvider(options =>
    {
        options.RedirectUri = "/lti/oidc/callback";
        options.Jwk = new JsonWebKey(config["Lti:Jwk"]);
    })
    .AddPlatforms(config.GetSection("Lti").GetSection("Platforms").Get<List<ToolPlatform>>());

builder.Services.AddAuthentication(static options => { options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme; })
    .AddCookie(static options =>
    {
        options.Cookie.SameSite = SameSiteMode.None;
        options.Events.OnRedirectToLogin = static context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    })
    .AddOAuth(Constants.CanvasDockerName, options =>
    {
        options.ClientId = config["Canvas:OAuth2:ClientId"];
        options.ClientSecret = config["Canvas:OAuth2:ClientSecret"];
        options.CallbackPath = "/login/canvas";
        options.AuthorizationEndpoint = builder.Configuration["Canvas:OAuth2:AuthorizationEndpoint"];
        options.TokenEndpoint = builder.Configuration["Canvas:OAuth2:TokenEndpoint"];
        options.SaveTokens = true;
    });

builder.Services.AddMemoryBasedRepository<LaunchSession>(static options =>
{
    options.Data = new Dictionary<object, LaunchSession>();
    options.ReadOnly = false;
});

builder.Services.AddMemoryBasedRepository<LaunchSessionCredentials>(static options =>
{
    options.Data = new Dictionary<object, LaunchSessionCredentials>();
    options.ReadOnly = false;
});

builder.Services.AddScoped<TokenClient>(static provider =>
{
    var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient();
    var options = provider.GetRequiredService<IOptionsSnapshot<OAuthOptions>>().Get(Constants.CanvasDockerName);

    return new TokenClient(httpClient, new TokenClientOptions
    {
        Address = options.TokenEndpoint,
        ClientId = options.ClientId,
        ClientSecret = options.ClientSecret,
    });
});

builder.Services.AddHttpClient(Constants.CanvasDockerName, (_, client) => { client.BaseAddress = new Uri(config["Canvas:ApiUrl"]); });
builder.Services.AddHttpClient<ICanvasCourseService, CanvasCourseService>(Constants.CanvasDockerName)
    .AddAccessTokenManagement(Constants.CanvasDockerName);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();