import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';

type Theme = 'light' | 'dark';
const ThemeContext = createContext<{ theme: Theme; toggle: () => void }>({ theme: 'dark', toggle: () => {} });

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setTheme] = useState<Theme>(() =>
    document.documentElement.classList.contains('dark') ? 'dark' : 'light',
  );

  useEffect(() => {
    document.documentElement.classList.toggle('dark', theme === 'dark');
    try {
      localStorage.setItem('sm-theme', theme);
    } catch {
      /* storage unavailable */
    }
  }, [theme]);

  const toggle = useCallback(() => setTheme((t) => (t === 'dark' ? 'light' : 'dark')), []);
  return <ThemeContext.Provider value={{ theme, toggle }}>{children}</ThemeContext.Provider>;
}

export const useTheme = () => useContext(ThemeContext);

/** Chart palette per theme (validated categorical slots + reserved status colors). */
export function useChartColors() {
  const { theme } = useTheme();
  const dark = theme === 'dark';
  return {
    series1: dark ? '#3987e5' : '#2a78d6',
    series2: dark ? '#d95926' : '#eb6834',
    series3: dark ? '#199e70' : '#1baf7a',
    grid: dark ? '#223047' : '#e2e8f0',
    axis: dark ? '#8d9bb1' : '#64748b',
    surface: dark ? '#111723' : '#ffffff',
    severity: {
      Critical: dark ? '#e66767' : '#d93b3b',
      High: dark ? '#d95926' : '#eb6834',
      Medium: dark ? '#c98500' : '#eda100',
      Low: dark ? '#3987e5' : '#2a78d6',
      Info: dark ? '#64748b' : '#94a3b8',
    } as Record<string, string>,
  };
}
