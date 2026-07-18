import type { ReactNode } from 'react'
import { useEffect } from 'react'
import {
  BrowserCacheLocation,
  InteractionRequiredAuthError,
  InteractionStatus,
  PublicClientApplication,
  type AccountInfo,
  type IPublicClientApplication,
} from '@azure/msal-browser'
import { MsalProvider, useMsal } from '@azure/msal-react'

export type EntraPublicConfiguration = {
  enabled: boolean
  authority: string
  clientId: string
  apiScope: string
}

let client: PublicClientApplication | null = null
let apiScope = ''

export async function loadAuthConfiguration(): Promise<EntraPublicConfiguration> {
  const response = await fetch('/api/auth/config')
  if (!response.ok) throw new Error(`Authentication configuration failed to load (${response.status}).`)
  return response.json() as Promise<EntraPublicConfiguration>
}

export async function initializeEntra(config: EntraPublicConfiguration): Promise<PublicClientApplication> {
  if (!config.authority || !config.clientId || !config.apiScope) throw new Error('Entra authentication is enabled but its public configuration is incomplete.')
  apiScope = config.apiScope
  client = new PublicClientApplication({
    auth: {
      authority: config.authority,
      clientId: config.clientId,
      redirectUri: window.location.origin,
      postLogoutRedirectUri: window.location.origin,
    },
    cache: { cacheLocation: BrowserCacheLocation.SessionStorage },
  })
  await client.initialize()
  const redirect = await client.handleRedirectPromise()
  const account = redirect?.account ?? client.getAllAccounts()[0]
  if (account) client.setActiveAccount(account)
  return client
}

export async function getApiAccessToken(): Promise<string | null> {
  if (!client) return null
  const account = client.getActiveAccount() ?? client.getAllAccounts()[0]
  if (!account) return null
  try {
    const response = await client.acquireTokenSilent({ account, scopes: [apiScope] })
    return response.accessToken
  } catch (error) {
    if (error instanceof InteractionRequiredAuthError) {
      await client.acquireTokenRedirect({ account, scopes: [apiScope] })
      return null
    }
    throw error
  }
}

function accountFor(instance: IPublicClientApplication, accounts: AccountInfo[]): AccountInfo | null {
  return instance.getActiveAccount() ?? accounts[0] ?? null
}

function EntraGate({ children }: { children: ReactNode }) {
  const { instance, accounts, inProgress } = useMsal()
  const account = accountFor(instance, accounts)

  useEffect(() => {
    if (!account && inProgress === InteractionStatus.None) {
      void instance.loginRedirect({ scopes: [apiScope] })
    }
  }, [account, inProgress, instance])

  if (!account) return <main className="auth-loading">Signing in with Microsoft Entra…</main>
  if (!instance.getActiveAccount()) instance.setActiveAccount(account)
  return children
}

export function EntraProvider({ instance, children }: { instance: PublicClientApplication; children: ReactNode }) {
  return <MsalProvider instance={instance}><EntraGate>{children}</EntraGate></MsalProvider>
}
