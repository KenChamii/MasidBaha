import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { AdminAuthService, LoginOutcome } from '../../core/api/admin-auth.service';
import { LoginPageComponent } from './login-page.component';

describe('LoginPageComponent', () => {
  let fixture: ComponentFixture<LoginPageComponent>;
  let login: ReturnType<typeof vi.fn>;
  const loginWithCode = vi.fn();
  const loginWithRecoveryCode = vi.fn();
  let navigateByUrl: ReturnType<typeof vi.spyOn>;

  async function setup(returnUrl: string | null, loginResult: Observable<LoginOutcome>) {
    login = vi.fn().mockReturnValue(loginResult);

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AdminAuthService, useValue: { login, loginWithCode, loginWithRecoveryCode } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(returnUrl ? { returnUrl } : {}) } }
        }
      ]
    });
    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    fixture = TestBed.createComponent(LoginPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  async function submit(email: string, password: string) {
    const root: HTMLElement = fixture.nativeElement;
    const emailInput = root.querySelector<HTMLInputElement>('input[name=email]')!;
    const passwordInput = root.querySelector<HTMLInputElement>('input[name=password]')!;

    emailInput.value = email;
    emailInput.dispatchEvent(new Event('input'));
    passwordInput.value = password;
    passwordInput.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('signs in with the typed credentials and goes to the page the user wanted', async () => {
    await setup('/reports?page=2', of('signed-in'));

    await submit('  a@test.local ', 'a-long-passphrase');

    expect(login).toHaveBeenCalledWith('a@test.local', 'a-long-passphrase');
    expect(navigateByUrl).toHaveBeenCalledWith('/reports?page=2');
  });

  it('goes to the home page when no return address was given', async () => {
    await setup(null, of('signed-in'));

    await submit('a@test.local', 'a-long-passphrase');

    expect(navigateByUrl).toHaveBeenCalledWith('/');
  });

  it.each(['//evil.example.net', 'https://evil.example.net', 'javascript:alert(1)'])(
    'ignores an off-site return address (%s)',
    async address => {
      await setup(address, of('signed-in'));

      await submit('a@test.local', 'a-long-passphrase');

      expect(navigateByUrl).toHaveBeenCalledWith('/');
    }
  );

  it('shows the message the API sent when sign-in fails', async () => {
    const failure = new HttpErrorResponse({ status: 401, error: { detail: 'Invalid email or password.' } });
    await setup(
      null,
      throwError(() => failure)
    );

    await submit('a@test.local', 'wrong-password');

    expect(fixture.nativeElement.querySelector('[role=alert]')?.textContent).toContain('Invalid email or password.');
    expect(navigateByUrl).not.toHaveBeenCalled();
  });

  it('explains rate limiting when the API gives no message', async () => {
    await setup(
      null,
      throwError(() => new HttpErrorResponse({ status: 429 }))
    );

    await submit('a@test.local', 'x');

    expect(fixture.nativeElement.querySelector('[role=alert]')?.textContent).toContain('Too many attempts');
  });

  it('does nothing until both fields are filled in', async () => {
    await setup(null, of('signed-in'));

    await submit('', '');

    expect(login).not.toHaveBeenCalled();
  });

  it('asks for the authenticator code when the account has two-factor on, then signs in', async () => {
    await setup(null, of('two-factor'));
    loginWithCode.mockReturnValue(of('signed-in'));

    await submit('a@test.local', 'a-long-passphrase');

    expect(navigateByUrl).not.toHaveBeenCalled();
    const root: HTMLElement = fixture.nativeElement;
    const codeInput = root.querySelector<HTMLInputElement>('input[name=code]')!;
    expect(codeInput).toBeTruthy();
    expect(root.querySelector('input[name=password]')).toBeNull();

    codeInput.value = '123456';
    codeInput.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(loginWithCode).toHaveBeenCalledWith('123456');
    expect(navigateByUrl).toHaveBeenCalledWith('/');
  });
});
