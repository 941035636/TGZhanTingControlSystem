# Third-party notices

## MeloTTS local Chinese provider

- Project: MeloTTS
- Upstream: https://github.com/myshell-ai/MeloTTS
- Version: v0.1.2
- Commit: `b633f243412169b999526e19eb6fcac0974b5d30`
- License: MIT; full text in `MeloTTS-LICENSE.txt`
- Local changes: `src/TtsWorker/MeloTtsLocal/melotts-v0.1.2-windows-offline.patch`

## MeloTTS Chinese model

- Source: https://huggingface.co/myshell-ai/MeloTTS-Chinese
- Revision: `082ca057e44f1e52ec47e1622a30286019e8a3ef`
- License declared by model repository: MIT
- `config.json` SHA-256: `d58b5acdab89ad2bbd65325affab309ae3cb964834b02f9a60587474e81c8bb9`
- `checkpoint.pth` SHA-256: `a74e9eadffff065c75eb6dfa040efa72cad23e72cfea70d39190bc174fb97093`

## BERT model dependency

- Source: https://huggingface.co/bert-base-multilingual-uncased
- Revision: `7cbf9a625e29989f6b9c6c2fa68234c304f7e38f`
- `config.json` SHA-256: `fba5d4b0a351a43f6ccb7a6587301fd9f6876ca36aae62af762af67c8f18db1c`
- `pytorch_model.bin` SHA-256: `2fec0e2a13cde5fa386fa00ba3e1bfea14b5d8fd8760f37f051799812a320e8d`
- `vocab.txt` SHA-256: `87b44292b452f6c05afa49b2e488e7eedf79ea4f4c39db6f2f4b37764228ef3f`

The deployment bundle also contains Python and pinned Python packages. Their license files and package metadata are
retained inside the generated runtime. Before distributing a final installer, the installer packaging process must
aggregate those notices; Phase 9E does not create the final MSI.
