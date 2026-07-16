using DeusaldStoryWeb;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Browser interop
builder.Services.AddScoped<IndexedDbInterop>();
builder.Services.AddScoped<WebFileDownloadInterop>();
builder.Services.AddScoped<WebProjectArchive>();

// Platform abstractions (see WebCommon)
builder.Services.AddScoped<IPreferencesStore, LocalStoragePreferencesStore>();
builder.Services.AddScoped<IProjectStoreFactory, IndexedDbProjectStoreFactory>();
builder.Services.AddScoped<IProjectLocationService, WebProjectLocationService>();
builder.Services.AddScoped<ILocalizationProjectPicker, WebLocalizationProjectPicker>();
builder.Services.AddScoped<IExcelInterop, WebExcelInterop>();
builder.Services.AddScoped<RecentProjectsStore>();

// UI localization (editor's own strings; per-user language preference)
builder.Services.AddScoped<UiLocalizationService>();

// Session state (one per app in WASM)
builder.Services.AddScoped<ProjectStateService>();

await builder.Build().RunAsync();