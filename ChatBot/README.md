# Simple OpenAI ChatBot Script

This repo is two Python Scripts that use Azure Key Vault (for the API key) and OpenAI.

The first script: Chat.py is a simple and short script to pull an API key in Key Vault, and then present the user with an input prompt. With this script you can type "exit" to end the script, or "json" to see the user side input that builds as the conversation continues, showing the "state" of the conversation that is sent to the API with each submission of user input.

The second script: Chat.WithMemSupport.py is the above script with the addition of a "memory" toggle. Using the "flip" command you can toggle memory on and off, so the conversation flips between stateful (remembering) and stateless (every use prompt is in isolation).

## Installation Instructions
I used the Windows Subsystem for Linux on Windows, but any OS with the required components should work.

### Prerequisites
1. Install the WSL and Ubuntu
1. Install Python3 and PIP
1. Install the AZ CLI
1. PIP install azure-identity and azure-keyvault-secrets
1. PIP install openai
1. Get an API key from OpenAI, store it in a Key Vault Secret
2. Update the scripts:
   1. line 5 with the Vault URI
   2. line 7 with the secret name


### Setup
1. Login fom the WSL to Azure: ```az login --use-device-code```
2. Run the script ```python3 chat.py```
