# Simple OpenAI ChatBot Script

This repo is two Python Scripts that use Azure Key Vault (for the API key) and OpenAI.

The first script: Chat.py is a simple and short script to pull an API key in Key Vault, and then present the user with an input prompt. With this script you can type "exit" to end the script, or "json" to see the user side input that builds as the conversation continues, showing the "state" of the conversation that is sent to the API with each submission of user input.

The second script: Chat.WithMemSupport.py is the above script with the addition of a "memory" toggle. Using the "flip" command you can toggle memory on and off, so the conversation flips between stateful (remembering) and stateless (every user prompt is in isolation).

## Installation Instructions
I used the Windows Subsystem for Linux on Windows and Ubuntu, but any OS with the required components should work.

### Prerequisites
1. Install the WSL and Ubuntu ([From HowToGeek](https://www.howtogeek.com/744328/how-to-install-the-windows-subsystem-for-linux-on-windows-11/))
1. Install Python3 and PIP (```sudo install python3 python3-pip```)
1. Install the AZ CLI ([Azure CLI on Linux](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli-linux?pivots=apt))
1. PIP install azure-identity, azure-keyvault-secrets, and openai (```pip install openai azure-identity azure-keyvault-secrets```)
   1. if you get a PIP error, you can install with apt (```sudo apt install python3-azure python3-openai```)
1. Get an API key from [OpenAI](https://platform.openai.com/), you may have to create an account:
   1. Login
   3. Navigate to your user profile, the select either "User API Keys" or "API Keys" under projects on the left nav menu (both work)
   4. Create a key, copy the secret key
   5. In another browser tab, go to [Key Vaults](https://portal.azure.com/#browse/Microsoft.KeyVault%2Fvaults) in the Azure Portal
   6. Pick a vault and create a new secret, pasting the secret key from above.
3. Copy and update the scripts:
   1. Open your Ubuntu instance using the terminal app
   2. Clone the github repo containing the scripts (```git clone https://github.com/tracsman/Projects.git```)
   3. Navigate to the ChatBot directory (```cd ./Projects/ChatBot```)
   4. Update the script file, with your specific API Key:
      1. Open the script in nano (```nano chat.py```)
      2. line 5 with the Vault URI
      3. line 7 with the secret name
      4. CTRL-X and save the script with your changes

### Running the script
1. Login fom the WSL to Azure: ```az login --use-device-code```
2. Run the script ```python3 chat.py```
