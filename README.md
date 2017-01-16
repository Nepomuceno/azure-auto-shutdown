# Azure Auto Shutdown

This project has as a goal to have one auto shutdown script for azure.
The idea of this script it is to be a simple but parallel script that can be executed anywhere ( hence it being a .Net core project)

This working by checking the AutoShutdownTag on the virtual machines and if it is present and the time falls withing the range it stop the virtual machine is outside of the range it starts the virtual machine.
To use it put this script to run as a cron job inside any of the services ( you can even deploy it as an azurewejob on a free tier and use it for free)

# Requirements

You will need to have 2 files in the same folder to use this application 

## cred.azure
This is the authentication file to connect to the azure account. You should be suing a server principal to authenticate to this account.
### Format:
```
subscription=[subscriptionid]
client=[serverPrincipalId]
key=[serverPrincipalPassword]
tenant=[tennatId]
```
You can have more details about how to create the authentication key [here](https://github.com/Azure/azure-sdk-for-net/blob/Fluent/AUTH.md)

## base.config.azure

This is the file with the configuration used to run the application.

### Format:
```
{
    "subscriptions" : [
                "[subscription to apply the auto shutdown]"
    ],
    "simulate": [bool]
}
```

