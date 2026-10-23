import type { Dictionaries } from './i18n.service';

// Texts used by the shared building blocks (toasts, dialogs, theme switch) in every app
export const SHARED_MESSAGES: Dictionaries = {
  en: {
    'common.close': 'Close',
    'common.dismiss': 'Dismiss',
    'theme.toLight': 'Switch to light mode',
    'theme.toDark': 'Switch to dark mode',
    'theme.light': 'Light mode',
    'theme.dark': 'Dark mode',
    'lang.switchToFil': 'Switch to Filipino',
    'lang.switchToEn': 'Switch to English',
    'err.network': "Can't reach the server. Please try again in a moment.",
    'err.tooMany': 'You are doing that too often. Please wait a moment and try again.',
    'err.server': 'Something went wrong on our side. Please try again.',
    'time.now': 'just now',
    'time.minutes': '{n}m ago',
    'time.hours': '{n}h ago',
    'time.days': '{n}d ago'
  },
  fil: {
    'common.close': 'Isara',
    'common.dismiss': 'Alisin',
    'theme.toLight': 'Lumipat sa light mode',
    'theme.toDark': 'Lumipat sa dark mode',
    'theme.light': 'Light mode',
    'theme.dark': 'Dark mode',
    'lang.switchToFil': 'Lumipat sa Filipino',
    'lang.switchToEn': 'Lumipat sa English',
    'err.network': 'Hindi maabot ang server. Subukan ulit sa ilang sandali.',
    'err.tooMany': 'Masyadong madalas ito. Maghintay sandali at subukan ulit.',
    'err.server': 'May problema sa aming panig. Subukan ulit.',
    'time.now': 'ngayon lang',
    'time.minutes': '{n}m ang nakalipas',
    'time.hours': '{n}h ang nakalipas',
    'time.days': '{n}d ang nakalipas'
  }
};
