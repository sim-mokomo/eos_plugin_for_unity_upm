/*
* Copyright (c) 2021 PlayEveryWare
* 
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
* 
* The above copyright notice and this permission notice shall be included in all
* copies or substantial portions of the Software.
* 
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
* SOFTWARE.
*/

// The SteamManager is designed to work with Steamworks.NET
// This file is released into the public domain.
// Where that dedication is not recognized you are granted a perpetual,
// irrevocable license to copy and modify this file as you see fit.
//
// Version: 1.0.12

#if !STEAMWORKS_MODULE || !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

using UnityEngine;
#if !DISABLESTEAMWORKS
using System.Collections;
using Steamworks;
#endif

//
// The SteamManager provides a base implementation of Steamworks.NET on which you can build upon.
// It handles the basics of starting up and shutting down the SteamAPI for use.
//
namespace PlayEveryWare.EpicOnlineServices.Samples.Steam
{
    [DisallowMultipleComponent]
    public class SteamManager : MonoBehaviour
    {
#if !DISABLESTEAMWORKS
        protected static bool s_EverInitialized = false;

        protected static SteamManager s_instance;
        public static SteamManager Instance
        {
            get
            {
                if (s_instance == null)
                {
                    return new GameObject("SteamManager").AddComponent<SteamManager>();
                }
                else
                {
                    return s_instance;
                }
            }
        }

        protected bool m_bInitialized = false;
        public static bool Initialized
        {
            get
            {
                return Instance.m_bInitialized;
            }
        }

        protected SteamAPIWarningMessageHook_t m_SteamAPIWarningMessageHook;

        [AOT.MonoPInvokeCallback(typeof(SteamAPIWarningMessageHook_t))]
        protected static void SteamAPIDebugTextHook(int nSeverity, System.Text.StringBuilder pchDebugText)
        {
            Debug.LogWarning(pchDebugText);
        }

#if UNITY_2019_3_OR_NEWER
        // In case of disabled Domain Reload, reset static members before entering Play Mode.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitOnPlayMode()
        {
            s_EverInitialized = false;
            s_instance = null;
        }
#endif

        //Enter Steam App ID here when available
        public const uint STEAM_APPID = 0;

        public string GetSteamID()
        {
            return SteamUser.GetSteamID().GetAccountID().ToString();
        }

        HAuthTicket authTicketHandle = HAuthTicket.Invalid;
        string authTicketString = null;
        public string GetAuthToken()
        {
            if (authTicketHandle != HAuthTicket.Invalid)
            {
                return authTicketString;
            }

            int bufferSize = 1024;
            byte[] buffer = new byte[bufferSize];
            uint ticketSize = 0;
            authTicketHandle = SteamUser.GetAuthSessionTicket(buffer, bufferSize, out ticketSize);
            if ((int)ticketSize > bufferSize)
            {
                SteamUser.CancelAuthTicket(authTicketHandle);
                bufferSize = (int)ticketSize;
                authTicketHandle = SteamUser.GetAuthSessionTicket(buffer, bufferSize, out ticketSize);
            }
            System.Array.Resize(ref buffer, (int)ticketSize);
            authTicketString = System.BitConverter.ToString(buffer).Replace("-", "");
            return authTicketString;
        }

        private void OnApplicationQuit()
        {
            if (authTicketHandle != HAuthTicket.Invalid)
            {
                SteamUser.CancelAuthTicket(authTicketHandle);
                authTicketHandle = HAuthTicket.Invalid;
                authTicketString = null;
            }
        }

        protected virtual void Awake()
        {
            // Only one instance of SteamManager at a time!
            if (s_instance != null)
            {
                Destroy(gameObject);
                return;
            }
            s_instance = this;

            if (s_EverInitialized)
            {
                // This is almost always an error.
                // The most common case where this happens is when SteamManager gets destroyed because of Application.Quit(),
                // and then some Steamworks code in some other OnDestroy gets called afterwards, creating a new SteamManager.
                // You should never call Steamworks functions in OnDestroy, always prefer OnDisable if possible.
                throw new System.Exception("Tried to Initialize the SteamAPI twice in one session!");
            }

            // We want our SteamManager Instance to persist across scenes.
            DontDestroyOnLoad(gameObject);

            if (!Packsize.Test())
            {
                Debug.LogError("[Steamworks.NET] Packsize Test returned false, the wrong version of Steamworks.NET is being run in this platform.", this);
            }

            if (!DllCheck.Test())
            {
                Debug.LogError("[Steamworks.NET] DllCheck Test returned false, One or more of the Steamworks binaries seems to be the wrong version.", this);
            }

            try
            {
                // If Steam is not running or the game wasn't started through Steam, SteamAPI_RestartAppIfNecessary starts the
                // Steam client and also launches this game again if the User owns it. This can act as a rudimentary form of DRM.

                // Once you get a Steam AppID assigned by Valve, you need to replace AppId_t.Invalid with it and
                // remove steam_appid.txt from the game depot. eg: "(AppId_t)480" or "new AppId_t(480)".
                // See the Valve documentation for more information: https://partner.steamgames.com/doc/sdk/api#initialization_and_shutdown
                AppId_t id = STEAM_APPID > 0 ? new AppId_t(STEAM_APPID) : AppId_t.Invalid;
                if (SteamAPI.RestartAppIfNecessary(id))
                {
                    Application.Quit();
                    return;
                }
            }
            catch (System.DllNotFoundException e)
            { // We catch this exception here, as it will be the first occurrence of it.
                Debug.LogError("[Steamworks.NET] Could not load [lib]steam_api.dll/so/dylib. It's likely not in the correct location. Refer to the README for more details.\n" + e, this);

                Application.Quit();
                return;
            }

            // Initializes the Steamworks API.
            // If this returns false then this indicates one of the following conditions:
            // [*] The Steam client isn't running. A running Steam client is required to provide implementations of the various Steamworks interfaces.
            // [*] The Steam client couldn't determine the App ID of game. If you're running your application from the executable or debugger directly then you must have a [code-inline]steam_appid.txt[/code-inline] in your game directory next to the executable, with your app ID in it and nothing else. Steam will look for this file in the current working directory. If you are running your executable from a different directory you may need to relocate the [code-inline]steam_appid.txt[/code-inline] file.
            // [*] Your application is not running under the same OS user context as the Steam client, such as a different user or administration access level.
            // [*] Ensure that you own a license for the App ID on the currently active Steam account. Your game must show up in your Steam library.
            // [*] Your App ID is not completely set up, i.e. in Release State: Unavailable, or it's missing default packages.
            // Valve's documentation for this is located here:
            // https://partner.steamgames.com/doc/sdk/api#initialization_and_shutdown
            m_bInitialized = SteamAPI.Init();
            if (!m_bInitialized)
            {
                Debug.LogError("[Steamworks.NET] SteamAPI_Init() failed. Refer to Valve's documentation or the comment above this line for more information.", this);

                return;
            }

            s_EverInitialized = true;
        }

        // This should only ever get called on first load and after an Assembly reload, You should never Disable the Steamworks Manager yourself.
        protected virtual void OnEnable()
        {
            if (s_instance == null)
            {
                s_instance = this;
            }

            if (!m_bInitialized)
            {
                return;
            }

            if (m_SteamAPIWarningMessageHook == null)
            {
                // Set up our callback to receive warning messages from Steam.
                // You must launch with "-debug_steamapi" in the launch args to receive warnings.
                m_SteamAPIWarningMessageHook = new SteamAPIWarningMessageHook_t(SteamAPIDebugTextHook);
                SteamClient.SetWarningMessageHook(m_SteamAPIWarningMessageHook);
            }
        }

        // OnApplicationQuit gets called too early to shutdown the SteamAPI.
        // Because the SteamManager should be persistent and never disabled or destroyed we can shutdown the SteamAPI here.
        // Thus it is not recommended to perform any Steamworks work in other OnDestroy functions as the order of execution can not be garenteed upon Shutdown. Prefer OnDisable().
        protected virtual void OnDestroy()
        {
            if (s_instance != this)
            {
                return;
            }

            s_instance = null;

            if (!m_bInitialized)
            {
                return;
            }

            SteamAPI.Shutdown();
        }

        protected virtual void Update()
        {
            if (!m_bInitialized)
            {
                return;
            }

            // Run Steam client callbacks
            SteamAPI.RunCallbacks();
        }
#else
	public static bool Initialized
    {
		get
        {
			return false;
		}
	}

    protected virtual void Awake()
    {
        Destroy(gameObject);
    }

    public string GetSteamID()
    {
        return null;
    }

    public string GetAuthToken()
    {
        return null;
    }

    public static SteamManager Instance
    {
        get
        {
#if !STEAMWORKS_MODULE
            Debug.LogError("Steamworks.NET plugin not installed. Install through the package manager from the git URL https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net");
#endif
            return null;
        }
    }
#endif // !DISABLESTEAMWORKS
    }
}