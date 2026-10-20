import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Check, Copy, Download, FileCode2 } from 'lucide-react';
import { networkApi } from '../../../lib/api';
import { cn } from '../../../lib/format';
import { Button } from '../../../components/ui/Button';
import { Spinner } from '../../../components/ui/DataTable';

/** Read-only viewer for the generated Linux configuration files. */
export function ConfigTab({ gatewayId, revision }: { gatewayId: string; revision: number }) {
  const config = useQuery({ queryKey: ['network', 'config', gatewayId, revision], queryFn: () => networkApi.config(gatewayId) });
  const [selected, setSelected] = useState(0);
  const [copied, setCopied] = useState(false);

  if (!config.data) return <Spinner />;
  const files = config.data.files;
  const file = files[Math.min(selected, files.length - 1)];
  const lines = file.content.replace(/\n$/, '').split('\n');

  const copy = async () => {
    await navigator.clipboard.writeText(file.content);
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
  };

  const downloadFile = () => {
    const url = URL.createObjectURL(new Blob([file.content.replace(/\r\n/g, '\n')], { type: 'text/plain' }));
    const a = Object.assign(document.createElement('a'), { href: url, download: file.path.split('/').pop()! });
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  };

  return (
    <div className="config-viewer">
      <nav className="config-files" aria-label="Generated files">
        <div className="section-title" style={{ padding: '0 10px' }}>Revision {config.data.revision}</div>
        {files.map((f, i) => (
          <button key={f.path} type="button" className={cn('config-file', i === selected && 'active')} onClick={() => setSelected(i)}>
            <FileCode2 size={14} />
            <span className="truncate">{f.path}</span>
          </button>
        ))}
      </nav>
      <div className="config-content">
        <div className="config-toolbar">
          <div style={{ minWidth: 0 }}>
            <code className="cell-title">{file.path}</code>
            <div className="cell-sub">{file.description}</div>
          </div>
          <span className="spacer" />
          <Button size="sm" icon={copied ? <Check size={14} /> : <Copy size={14} />} onClick={copy}>{copied ? 'Copied' : 'Copy'}</Button>
          <Button size="sm" icon={<Download size={14} />} onClick={downloadFile}>Download</Button>
        </div>
        <pre className="code-block" aria-label={`Contents of ${file.path}`}>
          {lines.map((line, i) => (
            <div key={i} className={cn('code-line', line.trimStart().startsWith('#') && 'comment', line.includes('[disabled]') && 'disabled')}>
              <span className="code-ln">{i + 1}</span>
              <span>{line || ' '}</span>
            </div>
          ))}
        </pre>
      </div>
    </div>
  );
}
