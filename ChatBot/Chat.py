import openai
from azure.keyvault.secrets import SecretClient
from azure.identity import DefaultAzureCredential

client = SecretClient(vault_url="https://LabSecrets.vault.azure.net",
                      credential=DefaultAzureCredential())
openai.api_key = client.get_secret("OpenAIKey").value

conversation_history  = [{"role": "system", 
                          "content": "You are a helpful but cranky assistant."}]

def chat_with_gpt(messages):
    response = openai.chat.completions.create(
        model="gpt-3.5-turbo",  # Use "gpt-3.5-turbo" for a cheaper option
        messages=messages)
    return response.choices[0].message.content

if __name__ == "__main__":
    print("ChatGPT CLI.\nType 'exit' to quit.\nType 'json' to see the conversation history.")
    while True:
        user_input = input("\nYou: ")
        if user_input.lower() == "exit": break
        if user_input.lower() == "json":
            for entry in conversation_history: print(entry)
            continue
        conversation_history.append({"role": "user", "content": user_input})
        response = chat_with_gpt(conversation_history)
        print("\nChatGPT:", response)
