using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Encryption;
using System;


// Alt:
// const string APP_USER = "username";
// const string MDB_PASSWORD = "password";
// const string connectionString = $"mongodb+srv://{APP_USER}:{MDB_PASSWORD}@cluster0.mgmrtgv.mongodb.net/?serverSelectionTimeoutMS=5000&tls=true";
var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING");
var cryptSharedLibPath = Environment.GetEnvironmentVariable("CRYPT_SHARED_lIB_PATH");

if ( connectionString is null || cryptSharedLibPath is null ) {
    Console.WriteLine("### ERROR:\n### Null value from required environment variable - please check your setenv script");
    Environment.Exit(1);
}

// Declare our key vault namespce
const string keyvaultDb = "__encryption";
const string keyvaultColl = "__keyVault";

// Could also read a key from file or randomly generate a key.
var b64LocalMasterKey = "Mng0NCt4ZHVUYUJCa1kxNkVyNUR1QURhZ2h2UzR2d2RrZzh0cFBwM3R6NmdWMDFBMUN3YkQ5aXRRMkhGRGdQV09wOGVNYUMxT2k3NjZKelhaQmRCZGJkTXVyZG9uSjFk";
var localMasterKey = Convert.FromBase64String(b64LocalMasterKey);
var keyvaultNamespace = new CollectionNamespace(keyvaultDb, keyvaultColl);

// Replace the provider type with KMS/KMIP for real-world use!
// Declare our key provider type
const string provider = "local";

// Declare our key provider attributes
var providerSettings = new Dictionary<string, object>
{
    { "key", localMasterKey }
};
var kmsProvider = new Dictionary<string, IReadOnlyDictionary<string, object>>
{
    { provider, providerSettings }
};

// Declare our database and collection
const string encryptedDbName = "companyData";
const string encryptedCollName = "employee";

// Instantiate our MongoDB Client object
var client = MdbClient(connectionString);

var (firstName, lastName) = GenerateName();

var payload = new BsonDocument
{
    {
        "name", new BsonDocument
        {
            { "firstName", firstName },
            { "lastName", lastName },
            { "otherNames", BsonNull.Value },
        }
    },
    {
        "address", new BsonDocument
        {
            { "streetAddress", "2 Bson Street" },
            { "suburbCounty", "Mongoville" },
            { "stateProvince", "Victoria" },
            { "zipPostcode", "3999" },
            { "country", "Oz" }
        }
    },
    { "dob", new DateTime(1980, 10, 11) },
    { "phoneNumber", "1800MONGO" },
    { "salary", 999999.99 },
    { "taxIdentifier", "78SD20NN001" },
    { "role", new BsonArray { "CIO" } }
};

// Retrieve the DEK UUID

var clientEncryptionOptions = new ClientEncryptionOptions(client, keyvaultNamespace, kmsProvider);
var clientEncryption = new ClientEncryption(clientEncryptionOptions);

var dataKey1 = await clientEncryption.GetKeyByAlternateKeyNameAsync("dataKey1");
if (dataKey1 is null) {
    // Different DataKeyOptions are needed for different providers
    await clientEncryption.CreateDataKeyAsync(
        provider, 
        new DataKeyOptions( alternateKeyNames: new [] { "dataKey1"}));
    dataKey1 = await clientEncryption.GetKeyByAlternateKeyNameAsync("dataKey1");
}

// With a known existing key you can fetch directly from the collection:
// var filter = Builders<BsonDocument>.Filter.Eq(d => d["keyAltNames"], "dataKey1");
// var dataKeyId_1 = (await (await client.GetDatabase(keyvaultDb).GetCollection<BsonDocument>(keyvaultColl).FindAsync(filter)).FirstOrDefaultAsync<BsonDocument>())["_id"];

var dataKeyId_1 = dataKey1["_id"];
if (dataKeyId_1.IsBsonNull)
{
    Console.WriteLine("Failed to find DEK");
    return;
}

var schema = new BsonDocument
{
    { "bsonType", "object" },
    {
        "encryptMetadata", new BsonDocument {
            { "keyId", new BsonArray { dataKeyId_1 } },
            { "algorithm", "AEAD_AES_256_CBC_HMAC_SHA_512-Random" }
        }
    },
    {
        "properties", new BsonDocument { {
            "name", new BsonDocument {
                { "bsonType", "object"} ,
                {
                    "properties", new BsonDocument { {
                        "firstName", new BsonDocument { {
                            "encrypt", new BsonDocument {
                                { "bsonType", "string" },
                                { "algorithm", "AEAD_AES_256_CBC_HMAC_SHA_512-Deterministic" }
                            }
                        } }
                    },
                    {
                        "lastName", new BsonDocument { {
                            "encrypt", new BsonDocument {
                                { "bsonType", "string" },
                                { "algorithm", "AEAD_AES_256_CBC_HMAC_SHA_512-Deterministic" }
                            }
                        } }
                    },
                    {
                        "otherNames", new BsonDocument { {
                            "encrypt", new BsonDocument { { "bsonType", "string" } }
                        } }
                    } }
                } }
            },
            {
                "address", new BsonDocument { {
                    "encrypt", new BsonDocument { { "bsonType", "object" } }
                } }
            },
            {
                "dob", new BsonDocument { {
                    "encrypt", new BsonDocument { { "bsonType", "date" } }
                } }
            },
            {
                "phoneNumber", new BsonDocument { {
                    "encrypt", new BsonDocument { { "bsonType", "string" } }
                } }
            },
            {
                "salary", new BsonDocument { {
                    "encrypt", new BsonDocument { { "bsonType", "double" } }
                } }
            },
            {
                "taxIdentifier", new BsonDocument { {
                    "encrypt", new BsonDocument { { "bsonType", "string" } }
                } }
            }
        }
    }
};
var schemaMap = new Dictionary<string, BsonDocument> { {"companyData.employee", schema } };

// cryptSharedLibPath must point to your unpacked library (.so or .dll) file
var extraOptions = new Dictionary<string, object>()
{
    { "cryptSharedLibPath", cryptSharedLibPath},
    { "cryptSharedLibRequired", true },
    { "mongocryptdBypassSpawn", true }
};
var autoEncryption = new AutoEncryptionOptions(
    kmsProviders: kmsProvider,
    keyVaultNamespace: keyvaultNamespace,
    schemaMap: schemaMap,
    extraOptions: extraOptions
);

var encryptedClient = MdbClient(connectionString, autoEncryption);

if (payload["name"]["otherNames"].IsBsonNull)
{
    payload["name"].AsBsonDocument.Remove("otherNames");
}

await encryptedClient.GetDatabase(encryptedDbName).GetCollection<BsonDocument>(encryptedCollName).InsertOneAsync(payload);
Console.WriteLine(payload["_id"]);

var filter1 = Builders<BsonDocument>.Filter.Eq(d => d["name.firstName"], firstName);
var decryptedDocs = await encryptedClient.GetDatabase(encryptedDbName).GetCollection<BsonDocument>(encryptedCollName).FindAsync(filter1);
await decryptedDocs.ForEachAsync(d => Console.WriteLine(d) );

static MongoClient MdbClient(string connectionString, AutoEncryptionOptions? options = null)
{
    var settings = MongoClientSettings.FromConnectionString(connectionString);
    settings.AutoEncryptionOptions = options;

    return new MongoClient(settings);
}

static (string, string) GenerateName()
{
    string[] firstNames = {"John","Paul","Ringo","George"};
    string[] lastNames = {"Lennon","McCartney","Starr","Harrison"};
    var firstName = firstNames[Random.Shared.Next(0, firstNames.Length)];
    var lastName = lastNames[Random.Shared.Next(0, lastNames.Length)];

    return (firstName, lastName);
}