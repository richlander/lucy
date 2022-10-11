using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using static System.Linq.Enumerable;
using static System.Console;

const string MCR_ADDRESS = "mcr.microsoft.com";
const string GHCR_ADDRESS = "ghcr.io";
const string DH_ADDRESS = "registry-1.docker.io";
const string MANIFEST_LIST_HEADER = "application/vnd.docker.distribution.manifest.list.v2+json";
const string MANIFEST_HEADER = "application/vnd.docker.distribution.manifest.v2+json";
const string API_VERSION = "v2";
const char TAG_CHAR = ':';
const char DOT_CHAR = '.';
const char SLASH_CHAR = '/';
const string LIBRARY = "library";
const string MANIFESTS = "manifests";
const string LATEST = "latest";
const string NULL = "null";
const string ERROR = "Something went wrong.";

Platform DefaultPlatform = new Platform(){Architecture = "amd64", Os = "linux"};
HttpClient client = new HttpClient();
string targetToken = string.Empty;
string baseToken = string.Empty;
int verboseLevel = 2;
bool verbose = verboseLevel > 0;
bool isImageFresh = false;

var targetTag = "mcr.microsoft.com/dotnet/samples:aspnetapp";
var baseTag = "mcr.microsoft.com/dotnet/aspnet:7.0";
var ghcrImage = "ghcr.io/richlander/dotnet-docker/aspnetapp:main";
targetTag = ghcrImage;
// var dhImage = "debian";

Image targetImage = GetImageForAddress(targetTag);
targetImage.Token = targetToken;
if (targetImage.Registry is GHCR_ADDRESS &&
    string.IsNullOrEmpty(targetImage.Token))
{
    targetImage.Token = await GetGhcrToken(targetImage);
}

ImageManifest targetImageManifest = await RequestImageFromRegistry(targetImage);

Image baseImage = GetImageForAddress(baseTag);
ImageManifest baseImageManifest = await RequestImageFromRegistry(baseImage);
baseImage.Token = baseToken;

if (verbose)
{
    WriteLine("Layer Update Check Yeoman v1.0");
    WriteLine($"Image: {targetTag}");
    WriteLine($"base: {baseTag}");
    WriteLine();
}

if (targetImageManifest.MediaType is MANIFEST_HEADER &&
    baseImageManifest.MediaType is MANIFEST_HEADER)
{
    if (verbose)
    {
        WriteLine($"Both images type: {MANIFEST_HEADER}");
    }

    isImageFresh = IsLayersMatch(baseImageManifest, targetImageManifest);
}
else if (targetImageManifest.MediaType is MANIFEST_HEADER &&
    baseImageManifest.MediaType is MANIFEST_LIST_HEADER)
{
    if (verbose)
    {
        WriteLine($"Image is type: {MANIFEST_HEADER}");
        WriteLine($"Base Image is type: {MANIFEST_LIST_HEADER}");
        WriteLine($"Validate for: {DefaultPlatform.Os}/{DefaultPlatform.Architecture}");
    }

    Manifest baseShaManifest = GetManifestFlavor(baseImageManifest, DefaultPlatform);
    baseImage.Tag = baseShaManifest.Digest;
    ImageManifest baseShaImageManifest = await RequestImageFromRegistry(baseImage);

    isImageFresh = IsLayersMatch(baseShaImageManifest, targetImageManifest);
}
else if (targetImageManifest.MediaType is MANIFEST_LIST_HEADER &&
    baseImageManifest.MediaType is MANIFEST_LIST_HEADER)
{
    if (verbose)
    {
        WriteLine($"Both images type: {MANIFEST_LIST_HEADER}");
        WriteLine($"Image includes {targetImageManifest.Manifests.Count} manifests that will be validated.");
    }
    // Iterate over target manifests and then compare with baseimage manifests
    foreach (var targetManifest in targetImageManifest.Manifests)
    {
        var baseShaManifest = GetManifestFlavor(baseImageManifest, targetManifest.Platform);
        baseImage.Tag = baseShaManifest.Digest;
        var baseShaImageManfest = await RequestImageFromRegistry(baseImage);
        targetImage.Tag = targetManifest.Digest;
        var targetShaImageManfest = await RequestImageFromRegistry(targetImage);

        if (verboseLevel > 1)
        {
            WriteLine();
            string version = targetManifest.Platform.OsVersion is object ? $":{targetManifest.Platform.OsVersion}" : string.Empty;
            WriteLine($"Validate for: {targetManifest.Platform.Os}{version}/{targetManifest.Platform.Architecture}");
        }

        bool match = IsLayersMatch(baseShaImageManfest, targetShaImageManfest);
        if (!match)
        {
            isImageFresh = match;
            break;
        }
    }

    isImageFresh = true;
}
else
{
    throw new Exception(ERROR);
}

WriteLine();
WriteLine("Image is fresh:");
WriteLine(isImageFresh);

return isImageFresh ? 0 : -1;

bool IsLayersMatch(ImageManifest lower, ImageManifest higher)
{
    if (lower.Layers is null or [] ||
        higher.Layers is null or [])
    {
        throw new Exception(ERROR);
    }

    foreach (int index in Range(0, lower.Layers.Count))
    {
        if (lower.Layers[index].Digest != higher.Layers[index].Digest)
        {
            if (verbose)
            {
                WriteLine($"Layer {index} doesn't match.");
                WriteLine($"Base image layer: {lower.Layers[index].Digest}");
                WriteLine($"Image layer: {higher.Layers[index].Digest}");
            }
            return false;
        }

        if (verboseLevel > 1)
        {
            WriteLine($"Layer match: {lower.Layers[index].Digest}");
        }

    }

    return true;
}

Image GetImageForAddress(string address)
{
    int firstDot = address.IndexOf(DOT_CHAR);
    int firstSlash = address.IndexOf(SLASH_CHAR);
    string registry = string.Empty;
    string repo = string.Empty;

    int tagStart = address.IndexOf(TAG_CHAR);
    string tag = tagStart > 0 ? address.Substring(tagStart + 1) : string.Empty;

    if (firstDot > 0 && firstSlash > firstDot)
    {
        registry = address.Substring(0, firstSlash);
        repo = tagStart > 0 ? address.Substring(registry.Length + 1, tagStart - registry.Length - 1) : address.Substring(registry.Length + 1);
    }
    else if (address.StartsWith(LIBRARY))
    {
        registry = DH_ADDRESS;
        repo = tagStart > 0 ? address.Substring(LIBRARY.Length + 1, tagStart - LIBRARY.Length - 1) : address.Substring(LIBRARY.Length + 1);
    }
    else
    {
        registry = DH_ADDRESS;
        repo = tagStart > 0 ? address.Substring(0, tagStart) : address;
    }

    if (tag == string.Empty && registry is not GHCR_ADDRESS)
    {
        tag = LATEST;
    }

    Image image = new()
    {
        Address = address,
        Registry = registry,
        Repo = repo,
        Tag = tag
    };

    return image;
}

Manifest GetManifestFlavor(ImageManifest manifestList, Platform platform)
{
    foreach (Manifest manifest in manifestList.Manifests)
    {
        if (manifest.Platform.Architecture == platform.Architecture &&
            manifest.Platform.Os == platform.Os &&
            manifest.Platform.OsVersion == platform.OsVersion)
            {
                return manifest;
            }
    }

    return new Manifest();
}

async Task<ImageManifest> RequestImageFromRegistry(Image image)
{
    // string tag = string.IsNullOrEmpty(image.Tag) ? string.Empty : $":{image.Tag}";
    string url = $"https://{image.Registry}/{API_VERSION}/{image.Repo}/{MANIFESTS}/{image.Tag}";
    
    using HttpRequestMessage request = new(HttpMethod.Get, url);
    request.Headers.Accept.Add(GetHeader(MANIFEST_LIST_HEADER));
    request.Headers.Accept.Add(GetHeader(MANIFEST_HEADER));

    if (!string.IsNullOrEmpty(image.Token))
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", image.Token);
    }

    var response = await client.SendAsync(request);

    JsonSerializerOptions options = new();
    ImageManifest? manifest = null;

    if (response.IsSuccessStatusCode && response.Content is not null)
    {
        manifest = await response.Content.ReadFromJsonAsync<ImageManifest>();
    }

    return manifest ?? new ImageManifest();
}

MediaTypeWithQualityHeaderValue GetHeader(string value) => new(new(value), 0.5);

async Task<string> GetGhcrToken(Image image)
{
    var def = new {Token = string.Empty};
    string url = $"https://{GHCR_ADDRESS}/token?scope=repository:{image.Repo}:pull";
    var token = await client.GetFromJsonAsync<RegistryToken>(url);
    return token?.Token ?? string.Empty;
}

class RegistryToken
{
    public string? Token { get; set; }
}

 class Image
 {
    public string? Address { get; set; }
    public string? Registry { get; set; }
    public string? Repo { get; set; }
    public string? Tag { get; set; }
    public string? Token { get; set; }
    public string? Architecture { get; set; }
    public string? Os { get; set; }
 }

class ImageManifest
{
    public int SchemaVersion { get; set; }
    public string? MediaType { get; set; }
    public List<Layer>? Layers { get; set; }
    public List<Manifest>? Manifests { get; set; }
}

class Layer
{
    public string? MediaType { get; set; }
    public string? Digest { get; set; }
}

class Manifest
{
    public string? MediaType { get; set; }
    public string? Digest { get; set; }
    public Platform? Platform { get; set; }
}

class Platform
{
    public string? Architecture { get; set; }
    public string? Os { get; set; }
    [JsonPropertyName("os.version")]
    public string? OsVersion { get; set; }
}
