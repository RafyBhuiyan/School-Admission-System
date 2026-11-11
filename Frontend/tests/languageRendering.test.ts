import assert from 'node:assert/strict'
import { after, test } from 'node:test'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { createServer } from 'vite'
import { localeFor, translate } from '../src/language.ts'
import type { Language } from '../src/language.ts'

// Vite compiles the actual TSX components; no browser profile or external server is used.
const server = await createServer({ server: { middlewareMode: true }, appType: 'custom' })
after(() => server.close())
const { default: App } = await server.ssrLoadModule('/src/App.tsx')
const { AdminPanel } = await server.ssrLoadModule('/src/AdminPanel.tsx')
const { StartupError } = await server.ssrLoadModule('/src/StartupError.tsx')
const { LanguageContext } = await server.ssrLoadModule('/src/languageContext.ts')
const { ApiProvider } = await server.ssrLoadModule('/src/ApiProvider.tsx')
const { createAdmissionApi } = await server.ssrLoadModule('/src/api/client.ts')
const api = createAdmissionApi('http://unused.test')
function render(language: Language, component: ReturnType<typeof createElement>) {
  const locale = localeFor(language)
  return renderToStaticMarkup(createElement(LanguageContext.Provider, { value: {
    language, setLanguage: () => {}, t: (text: string, values?: Record<string, string | number>) => translate(language, text, values),
    date: (value: string) => new Date(value).toLocaleString(locale), number: (value: number) => value.toLocaleString(locale),
  } }, createElement(ApiProvider, { api }, component)))
}
for (const language of ['en', 'bn'] as const) {
  test(`student portal renders ${language} labels and a button for the other language`, () => {
    const html = render(language, createElement(App))
    for (const label of ['Birth registration number', 'Admission classes', 'Admin', 'No admission classes yet']) assert.ok(html.includes(translate(language, label)), label)
    assert.ok(html.includes(language === 'en' ? 'Switch to বাংলা' : 'Switch to English'))
    assert.ok(!html.includes(language === 'en' ? 'জন্মনিবন্ধন নম্বর</label>' : 'Birth registration number</label>'))
  })
  test(`admin sign-in renders ${language} labels`, () => {
    const html = render(language, createElement(AdminPanel, { catalog: {}, onClose: () => {} }))
    for (const label of ['Administration', 'Admin code', 'Continue']) assert.ok(html.includes(translate(language, label)), label)
  })
  test(`startup failure remains readable and switchable in ${language}`, () => {
    const html = render(language, createElement(StartupError))
    assert.ok(html.includes(translate(language, 'Unable to start')))
    assert.ok(html.includes(language === 'en' ? 'Switch to বাংলা' : 'Switch to English'))
  })
}
