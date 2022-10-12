using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using static System.Linq.Enumerable;
using static System.Console;

const char TAG_CHAR = ':';
const char DOT_CHAR = '.';
const char SLASH_CHAR = '/';
const string GHCR_ADDRESS = "ghcr.io";
const string DH_ADDRESS = "index.docker.io";
const string DH_LIBRARY = "library";
const string LATEST = "latest";
const string MANIFEST_LIST_HEADER = "application/vnd.docker.distribution.manifest.list.v2+json";
const string MANIFEST_HEADER = "application/vnd.docker.distribution.manifest.v2+json";
const string API_VERSION = "v2";
const string MANIFESTS = "manifests";
const string ERROR = "Something went wrong.";
const string FRESH = "fresh";
const string STALE = "stale";

/*
**CLI args**
Required:
[image] [string]
[baseImage] [string]

Optional:
--imageOs [string]
--imageArch [string]
--imageToken [string]
--baseImageToken [string]
--verbosity [int] 0-2
*/

var targetTag = string.Empty;
var baseTag = string.Empty;
// tokens are needed for private repos
// GHCR and DH also require tokens for public repos
string? targetToken = null;
string? baseToken = null;
int verboseLevel = 0;

if (args.Length >= 2)
{
    targetTag = args[0];
    baseTag = args[1];
}
else
{
    WriteLine("Incorrect arguments were provided.");
    WriteHelp();
    return;
}

if (args.Length > 2)
{
    targetToken = GetArg("--imagetoken");
    baseToken = GetArg("--baseimagetoken");
    string? verbosity = GetArg("--verbosity");
    if (verbosity is not null)
    {
        int.TryParse(verbosity, out verboseLevel);
    }
}

HttpClient client = new HttpClient();
// Perhaps a way of setting this via args is needed
Platform DefaultPlatform = new Platform("amd64" ,"linux");
bool verbose = verboseLevel > 0;
bool isImageFresh = false;

if (verbose)
{
    WriteLine("Layer Update Check Yeoman (Lucy) v1.0");
    WriteLine($"Target image: {targetTag}");
    WriteLine($"Base image  : {baseTag}");
    WriteLine();
}

// Split the image address into components
// Load image from registry
// Save into various record types
Image targetImage = GetImageForAddress(targetTag);
targetImage.Token = targetToken;
ImageManifest targetImageManifest = await GetManifestFromRegistry(targetImage);
Image baseImage = GetImageForAddress(baseTag);
ImageManifest baseImageManifest = await GetManifestFromRegistry(baseImage);
baseImage.Token = baseToken;

// https://docs.docker.com/registry/spec/manifest-v2-2/
// Images can be of two main types:
// Basic images -- Manifest
// Multi-arch images -- Manifest lists
// Validation differs based on the image returned
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
    baseImageManifest.MediaType is MANIFEST_LIST_HEADER &&
    baseImageManifest.Manifests is not null)
{
    if (verbose)
    {
        WriteLine($"Image is type: {MANIFEST_HEADER}");
        WriteLine($"Base Image is type: {MANIFEST_LIST_HEADER}");
        WriteLine($"Validate for: {DefaultPlatform.Os}/{DefaultPlatform.Architecture}");
    } 

    // Get the manifest flavor that matches the platform of target image
    Manifest? baseShaManifest = GetManifestFlavor(baseImageManifest.Manifests, DefaultPlatform);

    if (baseShaManifest is null)
    {
        WriteLine($"Platform manifest cannot be found in base image: {DefaultPlatform}");
    }
    else
    {
        baseImage.Digest = baseShaManifest.Digest;
        ImageManifest baseShaImageManifest = await GetManifestFromRegistry(baseImage);

        isImageFresh = IsLayersMatch(baseShaImageManifest, targetImageManifest);
    }
}
else if (targetImageManifest.MediaType is MANIFEST_LIST_HEADER &&
    targetImageManifest.Manifests is not null &&
    baseImageManifest.MediaType is MANIFEST_LIST_HEADER &&
    baseImageManifest.Manifests is not null)
{
    if (verbose)
    {
        WriteLine($"Both images type: {MANIFEST_LIST_HEADER}");
        WriteLine($"Image includes {targetImageManifest.Manifests.Count} manifests that will be validated.");
    }
    // Iterate over target manifests and then compare with baseimage manifests
    foreach (var targetManifest in targetImageManifest.Manifests)
    {
        targetImage.Digest = targetManifest.Digest;
        var targetShaImageManifest = await GetManifestFromRegistry(targetImage);
        Manifest? baseShaManifest = GetManifestFlavor(baseImageManifest.Manifests, targetManifest.Platform);

        if (baseShaManifest is null)
        {
            WriteLine($"Platform manifest cannot be found in base image: {targetManifest.Platform}");
            isImageFresh = false;
            break;
        }

        baseImage.Digest = baseShaManifest.Digest;
        var baseShaImageManfest = await GetManifestFromRegistry(baseImage);

        if (verboseLevel > 1)
        {
            WriteLine();
            string version = targetManifest.Platform.OsVersion is object ? $":{targetManifest.Platform.OsVersion}" : string.Empty;
            WriteLine($"Validate for: {targetManifest.Platform.Os}{version}/{targetManifest.Platform.Architecture}");
        }

        bool match = IsLayersMatch(baseShaImageManfest, targetShaImageManifest);
        if (!match)
        {
            isImageFresh = match;
            break;
        }

        isImageFresh = true;
    }
}
else
{
    throw new Exception(ERROR);
}

if (verbose)
{
    WriteLine();
    WriteLine("Image state:");
}

WriteLine(isImageFresh ? FRESH : STALE);

bool IsLayersMatch(ImageManifest lower, ImageManifest higher)
{
    if (lower.Layers is null or [] ||
        higher.Layers is null or [])
    {
        throw new Exception(ERROR);
    }

    // Layers will match for fresh images
    // One or more layers will not match for stale images
    foreach (int index in Range(0, lower.Layers.Count))
    {
        if (lower.Layers[index].Digest != higher.Layers[index].Digest)
        {
            if (verbose)
            {
                WriteLine($"Layer {index} doesn't match.");
                WriteLine($"Image layer: {higher.Layers[index].Digest}");
                WriteLine($"Base image layer: {lower.Layers[index].Digest}");
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

// Break address into component parts
// Translate Docker Hub addresses into canonical form
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
    else if (!address.Contains(SLASH_CHAR))
    {
        registry = DH_ADDRESS;
        repo = tagStart > 0 ? address.Substring(0, tagStart) : address;
        repo = $"{DH_LIBRARY}/{repo}";
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

    Image image = new(address, registry, repo, tag);

    return image;
}

// Get manifest for a given platform
Manifest? GetManifestFlavor(List<Manifest> manifestList, Platform platform) => manifestList.FirstOrDefault((m) => m.Platform == platform);

// Get Manifest from registry
async Task<ImageManifest> GetManifestFromRegistry(Image image)
{
    // Get a token from GHRC or DH if one is missing
    // Assumes that the image is public; otherwise, this will fail
    if (image.Registry is GHCR_ADDRESS &&
        image.Token is null)
    {
        image.Token = await GetGhcrToken(image);
    }
    else if (image.Registry is DH_ADDRESS &&
        image.Token is null)
    {
        image.Token = await GetDhcrToken(image);
    }

    string tag = image.Digest ?? image.Tag;
    string url = $"https://{image.Registry}/{API_VERSION}/{image.Repo}/{MANIFESTS}/{tag}";
    
    using HttpRequestMessage request = new(HttpMethod.Get, url);
    request.Headers.Accept.Add(GetHeader(MANIFEST_LIST_HEADER));
    request.Headers.Accept.Add(GetHeader(MANIFEST_HEADER));

    if (!string.IsNullOrEmpty(image.Token))
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", image.Token);
    }

    var response = await client.SendAsync(request);

    ImageManifest? manifest = null;

    if (response.IsSuccessStatusCode)
    {
        manifest = await response.Content.ReadFromJsonAsync<ImageManifest>();
    }

    return manifest ?? throw new Exception($"Registry responded with {response.StatusCode} and message: {response.RequestMessage}");
}

MediaTypeWithQualityHeaderValue GetHeader(string value) => new(new(value), 0.5);

// Public GHCR images require auth
async Task<string> GetGhcrToken(Image image)
{
    string url = $"https://{GHCR_ADDRESS}/token?scope=repository:{image.Repo}:pull";
    RegistryToken? token = await client.GetFromJsonAsync<RegistryToken>(url);
    return token?.Token ?? throw new Exception("Registry did not return valid token. Consider providing a token.");
}

// Public DH images require auth
async Task<string> GetDhcrToken(Image image)
{
    string url = $"https://auth.docker.io/token?service=registry.docker.io&scope=repository:{image.Repo}:pull";
    RegistryToken? token = await client.GetFromJsonAsync<RegistryToken>(url);
    return token?.Token ?? throw new Exception("Registry did not return valid token. Consider providing a token.");
}

string? GetArg(string arg)
{
    foreach (int index in Range(2, args.Length - 2))
    {
        bool last = index == args.Length - 1;
        if (arg == args[index].ToLowerInvariant() &&
        !last)
        {
            return args[index + 1];
        }
    }

    return null;
}

void WriteHelp()
{
WriteLine("""
**CLI args**
Required:
[image] [string]
[baseImage] [string]

Optional:
--imageOs [string]
--imageArch [string]
--imageToken [string]
--baseImageToken [string]
--verbosity [int] 0-2
""");
}

// Records that describe images and manifest object model
// Primarily for JSON deserialization
record RegistryToken(string? Token);

record Image(string? Address, string Registry, string Repo, string Tag)
{
    public string? Digest { get; set; }
    public string? Token { get; set; }
    public string? Architecture { get; set; }
    public string? Os { get; set; }
};

record ImageManifest(int SchemaVersion, string MediaType)
{
    public List<Layer>? Layers { get; set; }
    public List<Manifest>? Manifests { get; set; }
};

record Layer(string MediaType, string Digest);

record Manifest(string MediaType, string Digest, Platform Platform);

record Platform(string Architecture, string Os)
{
    [JsonPropertyName("os.version")]
    public string? OsVersion { get; set; }
};
