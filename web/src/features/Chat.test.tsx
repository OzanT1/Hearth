import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { MessageBody } from './Chat';

describe('restricted message Markdown', () => {
  it('renders formatting and code without interpreting raw HTML', () => {
    const { container } = render(<MessageBody content={'**hello** `world`\n\n<script>alert(1)</script><img src="x" onerror="alert(1)" />'}/>);
    expect(screen.getByText('hello').tagName).toBe('STRONG');
    expect(screen.getByText('world').tagName).toBe('CODE');
    expect(container.querySelector('script')).toBeNull();
    expect(container.querySelector('img')).toBeNull();
  });
  it('blocks executable links and prevents external image tracking', () => {
    const { container } = render(<MessageBody content={'[click](javascript:alert%281%29) ![tracker](https://tracker.example/image.png)'}/>);
    expect(container.querySelector('a')).not.toHaveAttribute('href', expect.stringContaining('javascript:'));
    expect(container.querySelector('img')).toBeNull();
  });
  it('opens normal links without exposing the opener', () => {
    render(<MessageBody content={'[documentation](https://example.com)'}/>);
    expect(screen.getByRole('link')).toHaveAttribute('rel', 'noopener noreferrer');
  });
});
