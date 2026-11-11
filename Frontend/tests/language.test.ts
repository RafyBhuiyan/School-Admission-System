import assert from 'node:assert/strict'
import test from 'node:test'
import { readLanguage, saveLanguage, translate, localeFor, LANGUAGE_STORAGE_KEY } from '../src/language.ts'
import { bangla } from '../src/translations.ts'

test('English is the default for missing, invalid or unavailable storage', () => {
  for (const value of [null, '', 'fr', 'BN']) assert.equal(readLanguage({ getItem: () => value }), 'en')
  assert.equal(readLanguage(null), 'en')
  assert.equal(readLanguage({ getItem: () => { throw new Error('blocked') } }), 'en')
})
test('Bangla preference survives a fresh read without using student storage', () => {
  const values = new Map<string, string>([['school-admission.student-id', '00123456789012345']])
  const storage = { getItem: (key: string) => values.get(key) ?? null, setItem: (key: string, value: string) => { values.set(key, value) } }
  saveLanguage('bn', storage)
  assert.equal(values.get(LANGUAGE_STORAGE_KEY), 'bn')
  assert.equal(readLanguage(storage), 'bn')
  assert.equal(values.get('school-admission.student-id'), '00123456789012345')
  saveLanguage('en', storage)
  assert.equal(readLanguage(storage), 'en')
})
test('blocked writes do not prevent language switching', () => {
  assert.doesNotThrow(() => saveLanguage('bn', { setItem: () => { throw new Error('blocked') } }))
  assert.doesNotThrow(() => saveLanguage('en', null))
})
test('labels and already-visible errors can switch in both directions', () => {
  for (const message of ['Admin', 'Admission confirmed.', 'Enter exactly 17 digits from the birth registration number.']) {
    assert.notEqual(translate('bn', message), message)
    assert.equal(translate('en', message), message)
  }
})
test('interpolation preserves leading zeros, class names and literal user text', () => {
  assert.equal(translate('bn', 'Registration ending in {digits}', { digits: '0001' }), 'নিবন্ধন নম্বরের শেষ অংশ 0001')
  assert.equal(translate('en', 'Apply for {name}', { name: 'Class {total} / <b>' }), 'Apply for Class {total} / <b>')
  assert.equal(translate('bn', 'Apply for {name}', { name: 'Class 6' }), 'Class 6-এ আবেদন করুন')
  assert.equal(translate('bn', 'Unknown server message'), 'Unknown server message')
})
test('every translated template preserves its named parameters', () => {
  for (const [english, translated] of Object.entries(bangla)) {
    assert.ok(translated.trim(), english)
    assert.deepEqual([...english.matchAll(/\{(\w+)\}/g)].map(row => row[1]).sort(), [...translated.matchAll(/\{(\w+)\}/g)].map(row => row[1]).sort(), english)
  }
})
test('dates and display counts use the selected locale', () => {
  assert.equal(localeFor('en'), 'en-GB')
  assert.equal(localeFor('bn'), 'bn-BD')
  assert.equal(new Intl.NumberFormat(localeFor('bn')).format(123), '১২৩')
  assert.equal(new Intl.DateTimeFormat(localeFor('bn'), { year: 'numeric', timeZone: 'UTC' }).format(new Date('2026-11-11T00:00:00Z')), '২০২৬')
})
