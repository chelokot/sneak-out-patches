using System.Text;
using SneakOut.FriendInviteUnlock;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

const ulong hostSteamId = 76561198012345678;
const string partyId = "31b3e134-0d1e-4d72-834a-c435c8e53c89.nakama-0";
const string region = "eu-central";

var original = new SteamPartyJoinToken(hostSteamId, partyId, region);
Require(original.TryEncode(out var connectString), "valid party token did not encode");
Require(
    Encoding.UTF8.GetByteCount(connectString) <= SteamPartyJoinToken.MaximumConnectStringBytes,
    "party token exceeded Steam's connect-string budget");
Require(!connectString.Any(char.IsWhiteSpace), "party token was not safe as one launch argument");
Require(SteamPartyJoinToken.TryParse(connectString, out var parsed), "valid party token did not parse");
Require(parsed == original, "party token did not round-trip");

var launchCommand = $"-screen-fullscreen 1 \"{connectString}\" -logFile output.log";
Require(
    SteamPartyJoinToken.TryParse(launchCommand, out var commandLineToken) && commandLineToken == original,
    "Steam launch command did not yield the party token");
Require(
    SteamPartyJoinToken.TryExtract(new[] { "Sneak Out.exe", "-batchmode", connectString }, out var argumentToken)
    && argumentToken == original,
    "process arguments did not yield the party token");

var unicode = new SteamPartyJoinToken(hostSteamId, "party/with+symbols", "São-Paulo");
Require(unicode.TryEncode(out var unicodeText), "UTF-8 party token did not encode");
Require(
    SteamPartyJoinToken.TryParse(unicodeText, out var unicodeParsed) && unicodeParsed == unicode,
    "UTF-8 party token did not round-trip");

Require(
    !SteamPartyJoinToken.TryParse("+sneakout_join=so2.76561198012345678.cGFydHk.cmVnaW9u", out _),
    "unknown token protocol version was accepted");
Require(
    !SteamPartyJoinToken.TryParse("+sneakout_join=so1.0.cGFydHk.cmVnaW9u", out _),
    "zero host Steam id was accepted");
Require(
    !SteamPartyJoinToken.TryParse("+sneakout_join=so1.76561198012345678.!.cmVnaW9u", out _),
    "invalid base64url payload was accepted");
Require(
    !new SteamPartyJoinToken(hostSteamId, new string('p', 161), region).TryEncode(out _),
    "oversized party id was accepted");
Require(
    !new SteamPartyJoinToken(hostSteamId, partyId, "region\nargument").TryEncode(out _),
    "control characters were accepted in a token value");

Console.WriteLine("Friend invite token tests passed.");
