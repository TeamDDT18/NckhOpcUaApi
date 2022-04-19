using API.Data;
using API.Interfaces;
using API.TokenServices;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Newtonsoft.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using API.OPCUALayer;
using API.Models.OptionsModels;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<ITokenService, TokenService>();
var connectionStrings = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<DataContext>(options =>
{

    options.UseMySql(connectionStrings, ServerVersion.AutoDetect(connectionStrings));
});

//services.AddCors(options =>
//{
//    options.AddPolicy("AllowAllHeaders",
//        builder =>
//        {
//            builder.AllowAnyOrigin()
//                    .AllowAnyHeader()
//                    .AllowAnyMethod();
//        });
//});



//JSON Serializer
builder.Services.AddControllersWithViews()
                .AddJsonOptions(options =>
                options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles)
                .AddJsonOptions(options => options.JsonSerializerOptions.IgnoreReadOnlyProperties = true);

//Identity
//builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
//               .AddEntityFrameworkStores<ApplicationDbContext>()
//               .AddDefaultTokenProviders();
builder.Services.AddOptions();
builder.Services.Configure<OPCUAServersOptions>(builder.Configuration.GetSection("OPCUAServersOptions"));
// Register a singleton service managing OPC UA interactions
builder.Services.AddSingleton<IUaClientSingleton, UaClient>();
//builder.Services.AddSingleton<IMixingStationSingleton, Scada>(); //for scada


builder.Services.AddSignalR();
//add authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["TokenKey"])),
        ValidateIssuer = false,
        ValidateAudience = false,
    };
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();
