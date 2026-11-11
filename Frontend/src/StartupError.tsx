import { LanguageSwitcher } from './LanguageProvider.tsx'
import { useLanguage } from './languageContext.ts'
export function StartupError() {
  const { t } = useLanguage()
  return <main role="alert"><LanguageSwitcher /><h1>{t('Unable to start')}</h1><p>{t('Please refresh and try again. If the problem continues, contact the administrator.')}</p></main>
}
