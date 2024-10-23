# C# CSFLE Local Code

Sample locally runnable C# code for CSFLE auto-encryption.  Uses a local
provider for thre CMK so no need for  a KMS or KMIP device.  **For dev/demo only
- in production use KMS/KMIP!!!** 


## Usage

Set your connection string and the location of you crypt shared library as
environment variables (see the `setenv.sample.sh` and `setenv.sample.ps1`
scripts for examples) and then:

```
dotnet restore
dotnet run
```

That's all folks!
