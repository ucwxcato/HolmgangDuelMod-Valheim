Use:

  TEST_SERVER\start_holmgangduelmod_test.bat

  It will:

  - Copy the existing dedicated-server runtime into TEST_SERVER\server
  - Exclude existing plugins, configs, cache, and logs
  - Build the newest HolmgangDuelMod Release
  - Install only HolmgangDuelMod and Jötunn
  - Refuse to start if any unexpected plugin DLL is found
  - **Launch on 127.0.0.1:2463**
  - Use world holmgangduelmod_test